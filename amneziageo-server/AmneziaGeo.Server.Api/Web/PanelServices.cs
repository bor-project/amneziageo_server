using System.Net;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Wires the settings of the panel into the container and the pipeline.
/// </summary>
public static class PanelServices
{
    /// <summary>
    /// Registers the page of the panel.
    /// </summary>
    public static IServiceCollection AddPanel(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<PanelIndex>();

        return services;
    }

    /// <summary>
    /// Writes down the settings the panel started under when it holds none.
    /// </summary>
    public static WebApplication SeedPanel(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PanelStore>();
        store.SeedAsync(app.Services.GetRequiredService<PanelSettings>(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return app;
    }

    /// <summary>
    /// Puts the panel under its path and behind the name it answers to.
    /// </summary>
    public static WebApplication UsePanel(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var settings = app.Services.GetRequiredService<PanelSettings>();
        if (settings.Prefix.Length > 1)
        {
            var under = new PathString(settings.Prefix.TrimEnd('/'));
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == under)
                {
                    context.Response.Redirect(settings.Prefix);

                    return;
                }

                if (!context.Request.Path.StartsWithSegments(under))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;

                    return;
                }

                await next(context).ConfigureAwait(false);
            });

            app.UsePathBase(under);
        }

        if (settings.Domains.Count > 0)
        {
            app.Use(async (context, next) =>
            {
                if (Named(context, settings.Domains))
                {
                    await next(context).ConfigureAwait(false);

                    return;
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
            });
        }

        app.UseRouting();

        return app;
    }

    private static bool Named(HttpContext context, IReadOnlyList<string> domains)
    {
        foreach (var domain in domains)
        {
            if (string.Equals(context.Request.Host.Host, domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var caller = context.Connection.RemoteIpAddress;

        return caller is not null && IPAddress.IsLoopback(caller);
    }
}
