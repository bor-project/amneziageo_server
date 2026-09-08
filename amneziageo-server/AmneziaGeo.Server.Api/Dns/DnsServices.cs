using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// Wires the resolver into the container.
/// </summary>
public static class DnsServices
{
    /// <summary>
    /// Registers the resolver, the sets it feeds and the service that runs it.
    /// </summary>
    public static IServiceCollection AddResolver(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<DnsState>();
        services.AddSingleton(provider => new DnsSets(
            provider.GetRequiredService<IHostNetwork>(),
            provider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<DnsHost>();
        services.AddHostedService(provider => provider.GetRequiredService<DnsHost>());

        return services;
    }
}
