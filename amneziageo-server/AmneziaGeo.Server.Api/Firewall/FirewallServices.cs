using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Firewall;

/// <summary>
/// Registers what the ports of the panel are held open with.
/// </summary>
public static class FirewallServices
{
    /// <summary>
    /// Adds the firewall of the host and the applier the panel opens its ports through.
    /// </summary>
    public static IServiceCollection AddFirewall(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(provider => new FirewallHost(
            provider.GetRequiredService<IHostCommands>(),
            provider.GetRequiredService<IHostNetwork>()));
        services.AddScoped<FirewallApplier>();

        return services;
    }

    /// <summary>
    /// Opens the ports the panel keeps open as the server comes up.
    /// </summary>
    public static WebApplication SettleFirewall(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<FirewallApplier>()
            .SettleAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return app;
    }
}
