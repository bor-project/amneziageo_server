using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Traffic;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// Registers what the clients are put on the host with.
/// </summary>
public static class ClientServices
{
    /// <summary>
    /// Adds the interface files, the client service and the signals to the clients.
    /// </summary>
    public static IServiceCollection AddClients(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new InterfaceFileOptions();
        configuration.GetSection(InterfaceFileOptions.Section).Bind(options);

        var guard = new GuardOptions();
        configuration.GetSection(GuardOptions.Section).Bind(guard);

        var signal = new SignalOptions();
        configuration.GetSection(SignalOptions.Section).Bind(signal);

        services.AddSingleton(options);
        services.AddSingleton(guard);
        services.AddSingleton<InterfaceFile>();
        services.AddSingleton<TrafficLedger>();
        services.AddSingleton<ClientHost>();
        services.AddScoped<TemplateRefresher>();
        services.AddHostedService<ClientBoot>();
        services.AddSingleton<ClientGuard>();
        services.AddHostedService(provider => provider.GetRequiredService<ClientGuard>());
        services.AddHostedService<ClientMeter>();
        services.AddSingleton(provider =>
        {
            var host = provider.GetRequiredService<ClientHost>();
            var guard = provider.GetRequiredService<ClientGuard>();

            return new ClientSignals(
                provider.GetRequiredService<DisconnectSignal>(),
                (endpoint, client) => signal.Always
                    || (guard.Online(endpoint, client.PublicKey) ?? host.States(endpoint, [client])[0].IsOnline),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<ClientSignals>>());
        });

        return services;
    }
}
