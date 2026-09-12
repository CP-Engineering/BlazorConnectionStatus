namespace CPE.BlazorConnectionStatus;

/// <summary>
/// What the monitor currently knows about connectivity.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// Nothing has been observed yet — the monitor has not started, or its first probe has not
    /// come back. Distinct from <see cref="Offline"/> on purpose: a consumer that treats
    /// "not known yet" as "offline" will block or degrade during every startup.
    /// </summary>
    Unknown = 0,

    /// <summary>A server was reachable at the last observation.</summary>
    Online = 1,

    /// <summary>The adapter is down, or the probe could not reach the server.</summary>
    Offline = 2,
}
