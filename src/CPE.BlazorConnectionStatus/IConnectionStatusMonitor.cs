namespace CPE.BlazorConnectionStatus;

/// <summary>
/// Reports whether the app can reach a server, and raises an event when that changes.
/// </summary>
/// <remarks>
/// Register with <c>AddBlazorConnectionStatus</c> and call <see cref="StartAsync"/> once the app is
/// rendering — not from a constructor, where JS interop is not available yet.
/// </remarks>
public interface IConnectionStatusMonitor : IAsyncDisposable
{
    /// <summary>The last observed state. <see cref="ConnectionState.Unknown"/> until the first observation.</summary>
    ConnectionState State { get; }

    /// <summary>
    /// True only when <see cref="State"/> is <see cref="ConnectionState.Online"/>. Unknown reads as
    /// false here, so anything that must distinguish the two should read <see cref="State"/>.
    /// </summary>
    bool IsOnline { get; }

    /// <summary>Raised when <see cref="State"/> changes — never for a repeat of the same state.</summary>
    event EventHandler<ConnectionState>? StatusChanged;

    /// <summary>
    /// Starts listening and takes a first observation. Safe to call more than once; later calls do
    /// nothing.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Probes now and completes with the resulting state, rather than waiting for the interval.
    /// Use before anything irreversible that needs the server.
    /// </summary>
    Task<ConnectionState> CheckNowAsync(CancellationToken cancellationToken = default);
}
