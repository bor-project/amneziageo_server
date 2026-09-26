using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Proxy;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Where the files of the websocket fronts are written.
/// </summary>
public sealed class FrontOptions
{
    /// <summary>
    /// The section the settings are read from.
    /// </summary>
    public const string Section = "Proxy";

    /// <summary>
    /// The directory the fronts take their files from.
    /// </summary>
    public string Directory { get; set; } = ProxyDefaults.Directory;
}

/// <summary>
/// Registers the services of the endpoints.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Adds the hello, the measurement, the websocket fronts and the server that answers them.
    /// </summary>
    public static IServiceCollection AddEndpointServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new FrontOptions();
        configuration.GetSection(FrontOptions.Section).Bind(options);

        services.AddSingleton(options);
        services.AddSingleton<IProxyRunner>(provider => SystemdProxies.Runs
            ? new SystemdProxies(provider.GetRequiredService<IHostCommands>())
            : new ChildProxies(options.Directory));
        services.AddSingleton(provider =>
            new ProxyHost(
                provider.GetRequiredService<IHostNetwork>(),
                provider.GetRequiredService<IProxyRunner>(),
                options.Directory));
        services.AddSingleton<SpeedTickets>();
        services.AddSingleton<IHelloFeature, WebSocketOffer>();
        services.AddSingleton<IHelloFeature, RoutingOffer>();
        services.AddSingleton<IHelloFeature, InboundOffer>();
        services.AddSingleton<IHelloFeature, SpeedOffer>();
        services.AddSingleton<IHelloFeature, SubscriptionOffer>();
        services.AddSingleton<ServiceDesk>();
        services.AddSingleton<ServiceShare>();
        services.AddSingleton<ServiceServer>();
        services.AddHostedService(provider => provider.GetRequiredService<ServiceServer>());

        return services;
    }

    /// <summary>
    /// Answers the services of the port the panel shares with them, leaving the path of the panel to the panel.
    /// </summary>
    public static WebApplication UseEndpointServices(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var share = app.Services.GetRequiredService<ServiceShare>();
        var desk = app.Services.GetRequiredService<ServiceDesk>();
        var settings = app.Services.GetRequiredService<PanelSettings>();
        var under = settings.Prefix.Length > 1 ? new PathString(settings.Prefix.TrimEnd('/')) : PathString.Empty;
        app.Use(async (context, next) =>
        {
            var panel = under.HasValue && context.Request.Path.StartsWithSegments(under);
            if (!panel && share.Point is { } point && desk.Takes(context, point))
            {
                await desk.AnswerAsync(context, point).ConfigureAwait(false);

                return;
            }

            await next(context).ConfigureAwait(false);
        });

        return app;
    }
}
