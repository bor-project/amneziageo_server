namespace AmneziaGeo.Server.Api.Hello;

/// <summary>
/// Registers what the point of the server answers with.
/// </summary>
public static class HelloServices
{
    /// <summary>
    /// Adds the passes of the measurements, the addresses of the tunnels, the features and the server of the point.
    /// </summary>
    public static IServiceCollection AddHello(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new HelloOptions();
        configuration.GetSection(HelloOptions.Section).Bind(options);

        services.AddSingleton(options);
        services.AddSingleton<SpeedTickets>();
        services.AddSingleton<TunnelAddresses>();
        services.AddSingleton<IHelloFeature, SubscriptionOffer>();
        services.AddSingleton<IHelloFeature, SpeedOffer>();
        services.AddSingleton<HelloDesk>();
        services.AddSingleton<HelloServer>();
        services.AddHostedService(provider => provider.GetRequiredService<HelloServer>());

        return services;
    }
}
