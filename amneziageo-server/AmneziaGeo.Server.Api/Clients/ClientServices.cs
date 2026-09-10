using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// Registers what the clients are put on the host with.
/// </summary>
public static class ClientServices
{
    /// <summary>
    /// Adds the interface files and the client service.
    /// </summary>
    public static IServiceCollection AddClients(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new InterfaceFileOptions();
        configuration.GetSection(InterfaceFileOptions.Section).Bind(options);

        services.AddSingleton(options);
        services.AddSingleton<InterfaceFile>();
        services.AddSingleton<ClientHost>();
        services.AddScoped<TemplateRefresher>();
        services.AddHostedService<ClientBoot>();

        return services;
    }
}
