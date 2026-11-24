using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Udp;
using AmneziaGeo.Server.Api.Proxies;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// Registers what the endpoints are put on the host with.
/// </summary>
public static class ConfigServices
{
    /// <summary>
    /// Adds the endpoint host, the keeper and the watch of the interfaces, and the service that raises them at start.
    /// </summary>
    public static IServiceCollection AddEndpoints(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ConfigChangeReader>();
        services.AddSingleton<UdpPorts>();
        services.AddSingleton(provider => new SharedUdpHost(provider.GetRequiredService<UdpPorts>(),
            provider.GetRequiredService<IHostNetwork>(), provider.GetRequiredService<AmneziaGeo.Server.Awg.Device.IAwgDevices>(),
            provider.GetRequiredService<ILogger<SharedUdpHost>>(),
            Path.Combine(Path.GetDirectoryName(AmneziaGeo.Server.Dal.ServerDatabase.DefaultPath()) ?? ".", "udp")));
        services.AddSingleton<EndpointHost>();
        services.AddSingleton<EndpointKeeper>();
        services.AddSingleton<ConfigWatch>();
        services.AddHostedService<ConfigBoot>();

        return services;
    }
}
