using Microsoft.JSInterop;

namespace CPE.BlazorConnectionStatus;

/// <summary>
/// Loads the library's own ES module and drives one instance of it. The module ships inside the
/// package, so consumers add no script tag of their own.
/// </summary>
internal sealed class ConnectivityInterop : IConnectivityInterop
{
    private const string ModulePath =
        "./_content/CPE.BlazorConnectionStatus/connection-status.js";

    private readonly IJSRuntime _jsRuntime;

    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;
    private bool _disposed;

    public ConnectivityInterop(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
    }

    public async Task StartAsync(
        DotNetObjectReference<ConnectivityMonitor> callback,
        ConnectivityOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _module ??= await _jsRuntime
            .InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath)
            .ConfigureAwait(false);

        _handle = await _module.InvokeAsync<IJSObjectReference>(
            "create",
            cancellationToken,
            callback,
            new
            {
                pingUrl = options.PingUrl,
                pingIntervalMs = (int)options.PingInterval.TotalMilliseconds,
                pingTimeoutMs = (int)options.PingTimeout.TotalMilliseconds,
            }).ConfigureAwait(false);
    }

    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle is null)
        {
            throw new InvalidOperationException(
                "The connection status module has not been started.");
        }

        await _handle.InvokeVoidAsync("checkNow", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Every teardown below can race the page going away. A disconnected runtime is the normal
        // end of a session, not a fault worth surfacing to the consumer's DisposeAsync.
        if (_handle is not null)
        {
            try
            {
                await _handle.InvokeVoidAsync("dispose").ConfigureAwait(false);
                await _handle.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            _handle = null;
        }

        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            _module = null;
        }
    }
}
