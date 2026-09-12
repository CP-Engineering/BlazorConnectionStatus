namespace CPE.BlazorConnectionStatus;

/// <summary>
/// How the monitor decides whether the app is online.
/// </summary>
public sealed class ConnectivityOptions
{
    /// <summary>
    /// URL to probe with an HTTP HEAD request. Leave null to trust the browser's own
    /// online/offline events alone, which report whether the adapter has a network — not whether
    /// anything is reachable across it. Point this at something cheap on your own API; any 2xx
    /// counts as reachable.
    /// </summary>
    public string? PingUrl { get; set; }

    /// <summary>
    /// How often to re-probe while the browser believes it is online. Ignored when
    /// <see cref="PingUrl"/> is null. The browser raises no event when a working connection stops
    /// reaching the internet, so this interval is the only thing that notices.
    /// </summary>
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a probe may take before it counts as unreachable. Keep it well under
    /// <see cref="PingInterval"/> so probes cannot overlap.
    /// </summary>
    public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Throws if the combination cannot work at run time.</summary>
    /// <exception cref="ArgumentException">The interval or timeout is unusable.</exception>
    public void Validate()
    {
        if (PingUrl is null)
        {
            return;
        }

        if (PingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "PingInterval must be greater than zero when PingUrl is set.", nameof(PingInterval));
        }

        if (PingTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "PingTimeout must be greater than zero when PingUrl is set.", nameof(PingTimeout));
        }

        if (PingTimeout >= PingInterval)
        {
            throw new ArgumentException(
                "PingTimeout must be shorter than PingInterval, or probes overlap.", nameof(PingTimeout));
        }
    }
}
