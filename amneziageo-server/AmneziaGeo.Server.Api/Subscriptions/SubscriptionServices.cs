using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// Wires the subscriptions into the container and the pipeline.
/// </summary>
public static class SubscriptionServices
{
    /// <summary>
    /// Registers the server, the feed and the holds of the subscriptions.
    /// </summary>
    public static IServiceCollection AddSubscriptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<SubscriptionState>();
        services.AddSingleton<DeviceHolds>();
        services.AddSingleton<SubscriptionServer>();
        services.AddHostedService(provider => provider.GetRequiredService<SubscriptionServer>());
        services.AddScoped<SubscriptionFeed>();

        return services;
    }

    /// <summary>
    /// Answers the subscriptions that share the port of the panel.
    /// </summary>
    public static WebApplication UseSubscriptions(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var state = app.Services.GetRequiredService<SubscriptionState>();
        var panel = app.Services.GetRequiredService<PanelSettings>();
        var scopes = app.Services.GetRequiredService<IServiceScopeFactory>();
        app.Use(async (context, next) =>
        {
            var settings = state.Current;
            var path = context.Request.Path;
            if (settings.IsEnabled
                && settings.Port == panel.Port
                && (SubscriptionAnswer.Asked(path, settings) ?? SubscriptionAnswer.AskedHold(path, settings)) is not null)
            {
                await SubscriptionAnswer.WriteAsync(context, settings, scopes).ConfigureAwait(false);

                return;
            }

            await next(context).ConfigureAwait(false);
        });

        return app;
    }
}
