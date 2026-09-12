namespace CPE.BlazorConnectionStatus;

/// <summary>
/// What the monitor currently knows about connectivity.
/// </summary>
/// <remarks>
/// <para>
/// NAMING — "Connectivity", not "Connection". This was <c>ConnectionState</c> in 0.9.0, and the
/// name collided with <see cref="System.Data.ConnectionState"/>: any file with
/// <c>using System.Data</c> got <c>CS0104: ambiguous reference</c> and had to alias one of them.
/// That is most files in an app with a local database, which is most of this library's audience,
/// so the collision was ours to fix rather than every consumer's to work around. The public types
/// were renamed to match in 0.10.0 (<c>IConnectivityMonitor</c>, <c>ConnectivityOptions</c>), so
/// the vocabulary is consistent even though the package and namespace keep the
/// <c>ConnectionStatus</c> name they were published under.
/// </para>
/// <para>
/// "BrowserConnectionState" was considered and rejected: the whole point of this library is that
/// it does NOT report the browser's opinion. <c>navigator.onLine</c> is the browser's answer, and
/// the reason this exists is that the browser's answer is the wrong one.
/// </para>
/// </remarks>
public enum ConnectivityState
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
