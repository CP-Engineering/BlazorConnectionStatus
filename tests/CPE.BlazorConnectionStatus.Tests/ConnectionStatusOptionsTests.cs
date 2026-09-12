namespace CPE.BlazorConnectionStatus.Tests;

/// <summary>
/// Validation exists so a misconfiguration fails at startup instead of quietly producing
/// overlapping probes inside a browser where nobody is reading the console.
/// </summary>
public class ConnectionStatusOptionsTests
{
    [Fact]
    public void Defaults_are_a_usable_pair()
    {
        var options = new ConnectionStatusOptions();

        Assert.Null(options.PingUrl);
        Assert.Equal(TimeSpan.FromSeconds(30), options.PingInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PingTimeout);
        options.Validate();
    }

    [Fact]
    public void Without_a_ping_url_the_intervals_do_not_matter()
    {
        var options = new ConnectionStatusOptions
        {
            PingUrl = null,
            PingInterval = TimeSpan.Zero,
            PingTimeout = TimeSpan.Zero,
        };

        options.Validate();
    }

    [Fact]
    public void A_ping_url_with_a_sane_pair_validates()
    {
        var options = new ConnectionStatusOptions
        {
            PingUrl = "/healthz",
            PingInterval = TimeSpan.FromSeconds(15),
            PingTimeout = TimeSpan.FromSeconds(2),
        };

        options.Validate();
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-10, 5)]
    public void A_non_positive_interval_is_rejected(int intervalSeconds, int timeoutSeconds)
    {
        var options = new ConnectionStatusOptions
        {
            PingUrl = "/healthz",
            PingInterval = TimeSpan.FromSeconds(intervalSeconds),
            PingTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        var error = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Equal("PingInterval", error.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_timeout_is_rejected(int timeoutSeconds)
    {
        var options = new ConnectionStatusOptions
        {
            PingUrl = "/healthz",
            PingInterval = TimeSpan.FromSeconds(30),
            PingTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        var error = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Equal("PingTimeout", error.ParamName);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 30)]
    public void A_timeout_that_can_outlast_the_interval_is_rejected(
        int intervalSeconds,
        int timeoutSeconds)
    {
        var options = new ConnectionStatusOptions
        {
            PingUrl = "/healthz",
            PingInterval = TimeSpan.FromSeconds(intervalSeconds),
            PingTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        var error = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Equal("PingTimeout", error.ParamName);
    }
}
