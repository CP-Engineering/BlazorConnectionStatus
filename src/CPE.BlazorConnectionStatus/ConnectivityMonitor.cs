using Microsoft.JSInterop;

namespace CPE.BlazorConnectionStatus;

/// <summary>
/// Tracks connectivity by combining the browser's own online/offline events with an optional
/// HTTP probe. The browser alone only knows whether a network adapter is attached; the probe is
/// what answers the question the app actually cares about, which is whether the server responds.
/// </summary>
/// <remarks>
/// Register through <see cref="ServiceCollectionExtensions.AddBlazorConnectionStatus"/> rather
/// than constructing this directly. Nothing touches JavaScript until
/// <see cref="StartAsync(CancellationToken)"/> is called, so the type is safe to resolve during
/// prerendering.
/// </remarks>
public sealed class ConnectivityMonitor : IConnectivityMonitor
{
    private readonly IConnectivityInterop _interop;
    private readonly ConnectivityOptions _options;
    private readonly List<TaskCompletionSource<ConnectivityState>> _pending = new();

    private DotNetObjectReference<ConnectivityMonitor>? _selfReference;
    private bool _started;
    private bool _disposed;

    internal ConnectivityMonitor(IConnectivityInterop interop, ConnectivityOptions options)
    {
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public ConnectivityState State { get; private set; } = ConnectivityState.Unknown;

    /// <inheritdoc />
    public bool IsOnline => State == ConnectivityState.Online;

    /// <inheritdoc />
    public event EventHandler<ConnectivityState>? StatusChanged;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_started)
        {
            return;
        }

        _options.Validate();

        _started = true;
        _selfReference = DotNetObjectReference.Create(this);

        try
        {
            await _interop.StartAsync(_selfReference, _options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A failed start must not leave the monitor claiming to be running, or a retry would
            // be swallowed by the guard above.
            _started = false;
            _selfReference.Dispose();
            _selfReference = null;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ConnectivityState> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_started)
        {
            throw new InvalidOperationException(
                $"Call {nameof(StartAsync)} before {nameof(CheckNowAsync)}.");
        }

        var completion = new TaskCompletionSource<ConnectivityState>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pending)
        {
            _pending.Add(completion);
        }

        using var registration = cancellationToken.Register(
            static state =>
            {
                var (monitor, source) = ((ConnectivityMonitor, TaskCompletionSource<ConnectivityState>))state!;
                monitor.Forget(source);
                source.TrySetCanceled();
            },
            (this, completion));

        try
        {
            await _interop.CheckNowAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Forget(completion);
            throw;
        }

        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Receives one observation from the browser. Public only because JavaScript has to reach it;
    /// it is not part of the supported surface and may change.
    /// </summary>
    /// <param name="isOnline">What the browser and the probe, taken together, just concluded.</param>
    [JSInvokable]
    public void OnConnectionStatusObserved(bool isOnline)
    {
        if (_disposed)
        {
            return;
        }

        var observed = isOnline ? ConnectivityState.Online : ConnectivityState.Offline;
        var changed = State != observed;
        State = observed;

        TaskCompletionSource<ConnectivityState>[] waiting;
        lock (_pending)
        {
            waiting = _pending.ToArray();
            _pending.Clear();
        }

        foreach (var source in waiting)
        {
            source.TrySetResult(observed);
        }

        if (changed)
        {
            StatusChanged?.Invoke(this, observed);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        TaskCompletionSource<ConnectivityState>[] waiting;
        lock (_pending)
        {
            waiting = _pending.ToArray();
            _pending.Clear();
        }

        foreach (var source in waiting)
        {
            source.TrySetCanceled();
        }

        try
        {
            await _interop.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // The circuit or page is already gone; there is nothing left to tear down.
        }

        _selfReference?.Dispose();
        _selfReference = null;
        StatusChanged = null;
    }

    private void Forget(TaskCompletionSource<ConnectivityState> completion)
    {
        lock (_pending)
        {
            _pending.Remove(completion);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConnectivityMonitor));
        }
    }
}
