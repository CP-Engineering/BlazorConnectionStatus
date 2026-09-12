namespace CPE.BlazorConnectionStatus.Tests;

/// <summary>
/// The state machine, exercised without a browser. The JavaScript boundary is faked; everything
/// these tests cover is the part that decides what the app sees.
/// </summary>
public class ConnectivityMonitorTests
{
    private static (ConnectivityMonitor Monitor, FakeConnectivityInterop Interop) Create(
        ConnectivityOptions? options = null)
    {
        var interop = new FakeConnectivityInterop();
        var monitor = new ConnectivityMonitor(interop, options ?? new ConnectivityOptions());
        return (monitor, interop);
    }

    [Fact]
    public void State_before_start_is_Unknown_not_Offline()
    {
        var (monitor, _) = Create();

        Assert.Equal(ConnectivityState.Unknown, monitor.State);
        Assert.False(monitor.IsOnline);
    }

    [Fact]
    public async Task Constructing_the_monitor_touches_no_javascript()
    {
        var (_, interop) = Create();

        Assert.Equal(0, interop.StartCount);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task StartAsync_hands_the_configured_options_to_the_browser()
    {
        var options = new ConnectivityOptions
        {
            PingUrl = "/healthz",
            PingInterval = TimeSpan.FromSeconds(20),
            PingTimeout = TimeSpan.FromSeconds(3),
        };
        var (monitor, interop) = Create(options);

        await monitor.StartAsync();

        Assert.Equal(1, interop.StartCount);
        Assert.Same(options, interop.ReceivedOptions);
    }

    [Fact]
    public async Task StartAsync_is_idempotent()
    {
        var (monitor, interop) = Create();

        await monitor.StartAsync();
        await monitor.StartAsync();

        Assert.Equal(1, interop.StartCount);
    }

    [Fact]
    public async Task StartAsync_rejects_options_that_cannot_work()
    {
        var (monitor, interop) = Create(new ConnectivityOptions
        {
            PingUrl = "/healthz",
            PingInterval = TimeSpan.FromSeconds(5),
            PingTimeout = TimeSpan.FromSeconds(5),
        });

        await Assert.ThrowsAsync<ArgumentException>(() => monitor.StartAsync());
        Assert.Equal(0, interop.StartCount);
    }

    [Fact]
    public async Task A_failed_start_can_be_retried()
    {
        var (monitor, interop) = Create();
        interop.StartException = new InvalidOperationException("module blocked");

        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.StartAsync());

        interop.StartException = null;
        await monitor.StartAsync();

        Assert.Equal(2, interop.StartCount);
    }

    [Fact]
    public async Task An_online_observation_moves_the_state_and_raises_the_event()
    {
        var (monitor, interop) = Create();
        var raised = new List<ConnectivityState>();
        monitor.StatusChanged += (_, state) => raised.Add(state);

        await monitor.StartAsync();
        interop.Observe(isOnline: true);

        Assert.Equal(ConnectivityState.Online, monitor.State);
        Assert.True(monitor.IsOnline);
        Assert.Equal(new[] { ConnectivityState.Online }, raised);
    }

    [Fact]
    public async Task The_first_offline_observation_raises_the_event_even_though_Unknown_was_not_online()
    {
        var (monitor, interop) = Create();
        var raised = new List<ConnectivityState>();
        monitor.StatusChanged += (_, state) => raised.Add(state);

        await monitor.StartAsync();
        interop.Observe(isOnline: false);

        Assert.Equal(ConnectivityState.Offline, monitor.State);
        Assert.Equal(new[] { ConnectivityState.Offline }, raised);
    }

    [Fact]
    public async Task Repeated_observations_of_the_same_state_raise_one_event()
    {
        var (monitor, interop) = Create();
        var raised = new List<ConnectivityState>();
        monitor.StatusChanged += (_, state) => raised.Add(state);

        await monitor.StartAsync();
        interop.Observe(isOnline: true);
        interop.Observe(isOnline: true);
        interop.Observe(isOnline: true);

        Assert.Equal(new[] { ConnectivityState.Online }, raised);
    }

    [Fact]
    public async Task Every_transition_raises_an_event()
    {
        var (monitor, interop) = Create();
        var raised = new List<ConnectivityState>();
        monitor.StatusChanged += (_, state) => raised.Add(state);

        await monitor.StartAsync();
        interop.Observe(isOnline: true);
        interop.Observe(isOnline: false);
        interop.Observe(isOnline: true);

        Assert.Equal(
            new[] { ConnectivityState.Online, ConnectivityState.Offline, ConnectivityState.Online },
            raised);
    }

    [Fact]
    public async Task CheckNowAsync_before_StartAsync_is_a_programming_error()
    {
        var (monitor, _) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.CheckNowAsync());
    }

    [Fact]
    public async Task CheckNowAsync_completes_with_the_state_the_browser_answers()
    {
        var (monitor, interop) = Create();
        await monitor.StartAsync();
        interop.AutoAnswer = true;

        var result = await monitor.CheckNowAsync();

        Assert.Equal(ConnectivityState.Online, result);
        Assert.Equal(1, interop.CheckNowCount);
    }

    [Fact]
    public async Task CheckNowAsync_waits_for_an_answer_that_arrives_later()
    {
        var (monitor, interop) = Create();
        await monitor.StartAsync();

        var pending = monitor.CheckNowAsync();
        Assert.False(pending.IsCompleted);

        interop.Observe(isOnline: false);

        Assert.Equal(ConnectivityState.Offline, await pending);
    }

    [Fact]
    public async Task One_observation_answers_every_waiting_caller()
    {
        var (monitor, interop) = Create();
        await monitor.StartAsync();

        var first = monitor.CheckNowAsync();
        var second = monitor.CheckNowAsync();

        interop.Observe(isOnline: true);

        Assert.Equal(ConnectivityState.Online, await first);
        Assert.Equal(ConnectivityState.Online, await second);
    }

    [Fact]
    public async Task CheckNowAsync_observes_cancellation()
    {
        var (monitor, _) = Create();
        await monitor.StartAsync();

        using var cts = new CancellationTokenSource();
        var pending = monitor.CheckNowAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task A_failed_probe_request_does_not_leave_a_caller_waiting_forever()
    {
        var (monitor, interop) = Create();
        await monitor.StartAsync();
        interop.CheckNowException = new InvalidOperationException("page gone");

        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.CheckNowAsync());

        // The abandoned waiter must not steal the next real observation.
        interop.CheckNowException = null;
        var pending = monitor.CheckNowAsync();
        interop.Observe(isOnline: true);

        Assert.Equal(ConnectivityState.Online, await pending);
    }

    [Fact]
    public async Task DisposeAsync_tears_down_the_browser_side()
    {
        var (monitor, interop) = Create();
        await monitor.StartAsync();

        await monitor.DisposeAsync();

        Assert.Equal(1, interop.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        var (monitor, interop) = Create();
        await monitor.StartAsync();

        await monitor.DisposeAsync();
        await monitor.DisposeAsync();

        Assert.Equal(1, interop.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_releases_a_waiting_CheckNowAsync()
    {
        var (monitor, _) = Create();
        await monitor.StartAsync();

        var pending = monitor.CheckNowAsync();
        await monitor.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Using_a_disposed_monitor_says_so_plainly()
    {
        var (monitor, _) = Create();
        await monitor.StartAsync();
        await monitor.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => monitor.StartAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => monitor.CheckNowAsync());
    }

    [Fact]
    public async Task An_observation_after_disposal_is_ignored_rather_than_throwing()
    {
        // A timer callback already in flight when the page tears down lands here.
        var (monitor, _) = Create();
        await monitor.StartAsync();
        await monitor.DisposeAsync();

        monitor.OnConnectionStatusObserved(isOnline: true);

        Assert.Equal(ConnectivityState.Unknown, monitor.State);
    }
}
