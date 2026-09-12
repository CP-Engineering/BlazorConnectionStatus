using Microsoft.JSInterop;

namespace CPE.BlazorConnectionStatus;

/// <summary>
/// The JavaScript boundary, as an interface so the state machine can be tested without a browser.
/// Everything below this line needs a real page; everything above it does not.
/// </summary>
internal interface IConnectionStatusInterop : IAsyncDisposable
{
    /// <summary>Loads the module and starts listening. The callback receives every observation.</summary>
    Task StartAsync(
        DotNetObjectReference<ConnectionStatusMonitor> callback,
        ConnectionStatusOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Asks for one observation now. The answer arrives through the callback.</summary>
    Task CheckNowAsync(CancellationToken cancellationToken = default);
}
