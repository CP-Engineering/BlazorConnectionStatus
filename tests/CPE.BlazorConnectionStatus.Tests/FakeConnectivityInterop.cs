using Microsoft.JSInterop;

namespace CPE.BlazorConnectionStatus.Tests;

/// <summary>
/// Stands in for the browser. Records what the monitor asked for and lets a test push
/// observations back at whatever moment the test cares about.
/// </summary>
internal sealed class FakeConnectivityInterop : IConnectivityInterop
{
    private DotNetObjectReference<ConnectivityMonitor>? _callback;

    public int StartCount { get; private set; }

    public int CheckNowCount { get; private set; }

    public int DisposeCount { get; private set; }

    public ConnectivityOptions? ReceivedOptions { get; private set; }

    /// <summary>Set to have <see cref="StartAsync"/> fail, as a browser refusing the module would.</summary>
    public Exception? StartException { get; set; }

    /// <summary>Set to have <see cref="CheckNowAsync"/> fail.</summary>
    public Exception? CheckNowException { get; set; }

    /// <summary>When set, every CheckNow immediately answers with this state, as a live page would.</summary>
    public bool? AutoAnswer { get; set; }

    public Task StartAsync(
        DotNetObjectReference<ConnectivityMonitor> callback,
        ConnectivityOptions options,
        CancellationToken cancellationToken = default)
    {
        StartCount++;
        ReceivedOptions = options;

        if (StartException is not null)
        {
            return Task.FromException(StartException);
        }

        _callback = callback;
        return Task.CompletedTask;
    }

    public Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        CheckNowCount++;

        if (CheckNowException is not null)
        {
            return Task.FromException(CheckNowException);
        }

        if (AutoAnswer is bool answer)
        {
            Observe(answer);
        }

        return Task.CompletedTask;
    }

    /// <summary>Delivers one observation, exactly as the JavaScript module would.</summary>
    public void Observe(bool isOnline)
    {
        if (_callback is null)
        {
            throw new InvalidOperationException("Start the monitor before observing.");
        }

        _callback.Value.OnConnectionStatusObserved(isOnline);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}
