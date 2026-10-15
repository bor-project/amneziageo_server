using AmneziaGeo.Server.Routing.Host;

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

        services.AddSingleton<EndpointHost>();
        services.AddSingleton<EndpointKeeper>();
        services.AddSingleton<ConfigWatch>();
        services.AddHostedService<ConfigBoot>();

        return services;
    }
}
