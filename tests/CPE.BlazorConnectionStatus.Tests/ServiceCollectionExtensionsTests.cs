using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace CPE.BlazorConnectionStatus.Tests;

/// <summary>
/// Registration has to resolve without a browser, because Blazor builds the container long before
/// JS interop is usable.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public async Task Registration_resolves_a_monitor_without_any_javascript()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime, UnusableJSRuntime>();
        services.AddBlazorConnectionStatus();

        // Async disposal throughout: the monitor is IAsyncDisposable only, which is what Blazor
        // uses for scopes, and a synchronous container Dispose would throw because of it.
        await using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IConnectionStatusMonitor>();

        Assert.NotNull(monitor);
        Assert.Equal(ConnectionState.Unknown, monitor.State);
    }

    [Fact]
    public async Task The_monitor_is_scoped_so_each_circuit_gets_its_own()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime, UnusableJSRuntime>();
        services.AddBlazorConnectionStatus();

        await using var provider = services.BuildServiceProvider();
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<IConnectionStatusMonitor>(),
            second.ServiceProvider.GetRequiredService<IConnectionStatusMonitor>());

        Assert.Same(
            first.ServiceProvider.GetRequiredService<IConnectionStatusMonitor>(),
            first.ServiceProvider.GetRequiredService<IConnectionStatusMonitor>());
    }

    [Fact]
    public void The_configure_callback_runs()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime, UnusableJSRuntime>();

        var seen = false;
        services.AddBlazorConnectionStatus(options =>
        {
            seen = true;
            options.PingUrl = "/healthz";
        });

        Assert.True(seen);
    }

    [Fact]
    public void Bad_options_fail_at_registration_not_at_the_first_probe()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime, UnusableJSRuntime>();

        Assert.Throws<ArgumentException>(() => services.AddBlazorConnectionStatus(options =>
        {
            options.PingUrl = "/healthz";
            options.PingInterval = TimeSpan.FromSeconds(1);
            options.PingTimeout = TimeSpan.FromSeconds(30);
        }));
    }

    [Fact]
    public void Disposing_the_container_synchronously_is_refused_and_that_is_expected()
    {
        // Pinned deliberately. The monitor owns a JS handle, so it is IAsyncDisposable only.
        // Blazor disposes scopes asynchronously, but a host that calls the synchronous Dispose
        // will see this, and it should be a documented consequence rather than a surprise.
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime, UnusableJSRuntime>();
        services.AddBlazorConnectionStatus();

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IConnectionStatusMonitor>();

        Assert.Throws<InvalidOperationException>(() => provider.Dispose());
    }

    [Fact]
    public void A_null_collection_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.AddBlazorConnectionStatus(null!));
    }

    /// <summary>
    /// Fails if anything calls into it, which is the point: registration must not.
    /// </summary>
    private sealed class UnusableJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("JS interop is not available during registration.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            throw new InvalidOperationException("JS interop is not available during registration.");
    }
}
