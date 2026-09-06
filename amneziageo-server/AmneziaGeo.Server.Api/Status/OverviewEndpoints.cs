using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Core.Status;

namespace AmneziaGeo.Server.Api.Status;

/// <summary>
/// The route the overview of the host is read through.
/// </summary>
public static class OverviewEndpoints
{
    /// <summary>
    /// Registers the monitor of the host.
    /// </summary>
    public static IServiceCollection AddOverview(this IServiceCollection services) =>
        services.AddSingleton<SystemMonitor>();

    /// <summary>
    /// Starts the beat that fills the window of the overview.
    /// </summary>
    public static WebApplication StartOverview(this WebApplication app)
    {
        app.Services.GetRequiredService<SystemMonitor>().Start();

        return app;
    }

    /// <summary>
    /// Maps the overview route.
    /// </summary>
    public static IEndpointRouteBuilder MapOverview(this IEndpointRouteBuilder routes)
    {
        routes
            .MapGet("/api/overview", (SystemMonitor monitor) => Results.Ok(monitor.Report()))
            .RequireScope(Scopes.ReadState);

        return routes;
    }
}
