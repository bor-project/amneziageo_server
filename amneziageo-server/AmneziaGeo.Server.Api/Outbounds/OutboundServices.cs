using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Api.Balancers;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Carrier;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Probe;

namespace AmneziaGeo.Server.Api.Outbounds;

/// <summary>
/// How the server puts the routing on the host.
/// </summary>
public sealed class RoutingOptions
{
    /// <summary>
    /// The section the settings are read from.
    /// </summary>
    public const string Section = "Routing";

    /// <summary>
    /// Whether the host tools are run through sudo.
    /// </summary>
    public bool Sudo { get; set; }
}

/// <summary>
/// Registers what the outbounds are put on the host with.
/// </summary>
public static class OutboundServices
{
    /// <summary>
    /// Adds the host tools, the interface reader and the outbound service.
    /// </summary>
    public static IServiceCollection AddOutbounds(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RoutingOptions();
        configuration.GetSection(RoutingOptions.Section).Bind(options);

        services.AddSingleton(options);
        services.AddSingleton<IHostCommands>(new HostCommands(options.Sudo));
        services.AddSingleton<IHostNetwork, IpHostNetwork>();
        services.AddSingleton<IAwgDevices, AwgDevices>();
        services.AddSingleton(provider => Carriers(provider));
        services.AddSingleton<ProbeLive>();
        services.AddSingleton<IProbeLink, ProbeLink>();
        services.AddSingleton<ProbeRunner>();
        services.AddSingleton<OutboundHost>();
        services.AddSingleton<RoutePlans>();
        services.AddSingleton<BalanceLive>();
        services.AddScoped<RouteApplier>();
        services.AddScoped<RouteTester>();
        services.AddHostedService<OutboundBoot>();
        services.AddHostedService<BalanceWatch>();
        services.AddHostedService<ProbeWatch>();

        return services;
    }

    private static CarrierHost Carriers(IServiceProvider provider)
    {
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CarrierHost));

        return new CarrierHost((message, error) => Note(logger, message, error));
    }

    private static void Note(ILogger logger, string message, Exception? error)
    {
        if (error is null)
        {
            logger.LogInformation("{Message}", message);

            return;
        }

        logger.LogWarning(error, "{Message}", message);
    }
}
