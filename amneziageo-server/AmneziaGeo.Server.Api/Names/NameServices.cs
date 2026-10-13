using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Names;

namespace AmneziaGeo.Server.Api.Names;

/// <summary>
/// Wires the reading of names out of the connections into the container.
/// </summary>
public static class NameServices
{
    /// <summary>
    /// Registers the gate of the ruleset, the desk that turns names into addresses of the rules, the endpoints the
    /// clients arrive on, and the services that take the queue of the firewall and the TCP connections handed over.
    /// </summary>
    public static IServiceCollection AddNames(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new NameOptions();
        configuration.GetSection(NameOptions.Section).Bind(options);

        services.AddSingleton(options);
        services.AddSingleton<NameGate>();
        services.AddSingleton(provider => new NameDesk(
            provider.GetRequiredService<DnsSets>(),
            provider.GetRequiredService<IHostNetwork>()));

        services.AddSingleton<NameInbounds>();
        services.AddSingleton<NameQueue>();
        services.AddHostedService(provider => provider.GetRequiredService<NameQueue>());
        services.AddSingleton<NameRelay>();
        services.AddHostedService(provider => provider.GetRequiredService<NameRelay>());
        services.AddSingleton<NameState>();

        return services;
    }
}
