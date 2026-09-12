using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace CPE.BlazorConnectionStatus;

/// <summary>
/// Registration for <see cref="IConnectivityMonitor"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a scoped <see cref="IConnectivityMonitor"/>. Call
    /// <see cref="IConnectivityMonitor.StartAsync(CancellationToken)"/> once the app is
    /// running — typically in the layout's <c>OnAfterRenderAsync</c> — because registration
    /// deliberately touches no JavaScript.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional configuration. Without it the monitor follows the browser's online/offline events
    /// only, which tell you an adapter exists, not that the server answers. Set
    /// <see cref="ConnectivityOptions.PingUrl"/> to get the stronger signal.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The configured intervals cannot work together; see
    /// <see cref="ConnectivityOptions.Validate"/>.
    /// </exception>
    public static IServiceCollection AddBlazorConnectionStatus(
        this IServiceCollection services,
        Action<ConnectivityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ConnectivityOptions();
        configure?.Invoke(options);

        // Fail here, at startup, rather than on the first probe inside a browser where nobody is
        // watching the console.
        options.Validate();

        services.AddScoped<IConnectivityMonitor>(provider =>
        {
            var jsRuntime = provider.GetRequiredService<IJSRuntime>();
            return new ConnectivityMonitor(new ConnectivityInterop(jsRuntime), options);
        });

        return services;
    }
}
