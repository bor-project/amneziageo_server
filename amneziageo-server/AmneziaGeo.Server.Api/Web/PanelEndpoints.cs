using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Updates;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// The routes the settings of the panel are managed through.
/// </summary>
public static class PanelEndpoints
{
    /// <summary>
    /// Maps the routes of the settings of the panel.
    /// </summary>
    public static IEndpointRouteBuilder MapPanel(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGroup("/api/panel").RequireScope(Scopes.ReadState).MapGet("/", ReadAsync);

        var writing = routes.MapGroup("/api/panel").RequireScope(Scopes.ManageAccess);
        writing.MapPut("/", SaveAsync);
        writing.MapPost("/restart", Restart);

        return routes;
    }

    /// <summary>
    /// Maps the page of the panel under the path the panel sits at.
    /// </summary>
    public static IEndpointRouteBuilder MapPanelPage(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes
            .MapFallback("/api/{**rest}", () => Results.Json(new Failure("unknown-route", "there is no such route"), statusCode: StatusCodes.Status404NotFound))
            .ExcludeFromDescription();

        routes
            .MapFallback(async (HttpContext context, PanelIndex index) =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(index.Text()).ConfigureAwait(false);
            })
            .ExcludeFromDescription();

        return routes;
    }

    private static async Task<IResult> ReadAsync(
        PanelStore store,
        PanelSettings running,
        WebOptions options,
        CancellationToken ct)
    {
        var settings = await store.ReadAsync(ct).ConfigureAwait(false);

        return Results.Ok(PanelAnswers.Panel(settings, running, options));
    }

    private static async Task<IResult> SaveAsync(
        PanelRequest request,
        PanelStore store,
        PanelSettings running,
        WebOptions options,
        FirewallApplier firewall,
        UpdateCenter updates,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var draft = PanelAnswers.Draft(request);
        var logger = loggers.CreateLogger(typeof(PanelEndpoints));
        var fault = PanelRules.Check(draft) is null
            ? CertificateFiles.Check(draft.Certificate, draft.CertificateKey, logger)
            : null;
        if (fault is not null)
        {
            return Results.Json(new Failure(fault.Code, fault.Message), statusCode: StatusCodes.Status400BadRequest);
        }

        var before = await store.ReadAsync(ct).ConfigureAwait(false);
        var result = await store.SaveAsync(draft, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Record is null)
        {
            return Results.Json(new Failure(result.Code, result.Message), statusCode: StatusCodes.Status400BadRequest);
        }

        await firewall.SettleAsync(ct).ConfigureAwait(false);
        if (result.Record.Prereleases != before.Prereleases)
        {
            updates.Recheck();
        }

        return Results.Ok(PanelAnswers.Panel(result.Record, running, options));
    }

    private static IResult Restart(IHostApplicationLifetime life, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(PanelEndpoints));
        logger.LogInformation("the panel was told to start over");

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
            life.StopApplication();
        });

        return Results.Accepted();
    }
}
