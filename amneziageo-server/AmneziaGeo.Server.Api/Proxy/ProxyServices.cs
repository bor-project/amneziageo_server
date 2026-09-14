using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Proxy;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Proxy;

/// <summary>
/// Where the files of the proxies are written.
/// </summary>
public sealed class ProxyOptions
{
    /// <summary>
    /// The section the settings are read from.
    /// </summary>
    public const string Section = "Proxy";

    /// <summary>
    /// The directory the proxies take their files from.
    /// </summary>
    public string Directory { get; set; } = ProxyDefaults.Directory;
}

/// <summary>
/// Registers what the proxies are put on the host with.
/// </summary>
public static class ProxyServices
{
    /// <summary>
    /// Adds the proxy service and the applier the panel changes the proxies through.
    /// </summary>
    public static IServiceCollection AddProxies(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new ProxyOptions();
        configuration.GetSection(ProxyOptions.Section).Bind(options);

        services.AddSingleton(options);
        services.AddSingleton<IProxyRunner>(provider => SystemdProxies.Runs
            ? new SystemdProxies(provider.GetRequiredService<IHostCommands>())
            : new ChildProxies(options.Directory));
        services.AddSingleton(provider =>
            new ProxyHost(
                provider.GetRequiredService<IHostCommands>(),
                provider.GetRequiredService<IHostNetwork>(),
                options.Directory,
                provider.GetRequiredService<IProxyRunner>()));
        services.AddScoped<ProxyApplier>();

        return services;
    }

    /// <summary>
    /// Puts the proxies the panel holds on the host as the server comes up.
    /// </summary>
    public static WebApplication SettleProxies(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ProxyApplier>()
            .SettleAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return app;
    }
}
