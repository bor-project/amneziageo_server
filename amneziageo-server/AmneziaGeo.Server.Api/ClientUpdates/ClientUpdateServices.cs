using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Updates;

namespace AmneziaGeo.Server.Api.ClientUpdates;

/// <summary>
/// Wires the releases of the client into the container and the pipeline.
/// </summary>
public static class ClientUpdateServices
{
    /// <summary>
    /// Registers the state and the server of the releases.
    /// </summary>
    public static IServiceCollection AddClientUpdates(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ClientUpdateState>();
        services.AddSingleton<ClientUpdateServer>();
        services.AddHostedService(provider => provider.GetRequiredService<ClientUpdateServer>());

        return services;
    }

    /// <summary>
    /// Answers the releases that share the port of the panel.
    /// </summary>
    public static WebApplication UseClientUpdates(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var state = app.Services.GetRequiredService<ClientUpdateState>();
        var panel = app.Services.GetRequiredService<PanelSettings>();
        app.Use(async (context, next) =>
        {
            var settings = state.Current;
            if (settings.IsEnabled
                && settings.Own
                && settings.PortOr(panel.Port) == panel.Port
                && await UpdateApi.TryAnswerAsync(context, settings.Prefix, state.Store, asking => UpdateToken.Holds(asking, settings.Token)).ConfigureAwait(false))
            {
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        return app;
    }
}
