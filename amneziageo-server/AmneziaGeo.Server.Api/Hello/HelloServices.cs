namespace AmneziaGeo.Server.Api.Hello;

/// <summary>
/// Registers what the point of the server answers with.
/// </summary>
public static class HelloServices
{
    /// <summary>
    /// Adds the passes of the measurements and the addresses of the tunnels.
    /// </summary>
    public static IServiceCollection AddHello(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<SpeedTickets>();
        services.AddSingleton<TunnelAddresses>();

        return services;
    }
}
