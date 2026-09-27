using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Access;

namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// Wires the connection log into the container.
/// </summary>
public static class AccessServices
{
    /// <summary>
    /// Registers the gate of the ruleset, the name book, the file of the log and the service that fills it.
    /// </summary>
    public static IServiceCollection AddAccessLog(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var database = configuration["Database:Path"] is { Length: > 0 } set ? set : ServerDatabase.DefaultPath();

        services.AddSingleton<AccessGate>();
        services.AddSingleton(new AccessNames());
        services.AddSingleton(new AccessRecords(AccessRecords.PathNear(database)));
        services.AddSingleton<AccessHost>();
        services.AddHostedService(provider => provider.GetRequiredService<AccessHost>());

        return services;
    }
}
