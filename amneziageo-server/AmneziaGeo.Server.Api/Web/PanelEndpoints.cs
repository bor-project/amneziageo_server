using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Updates;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
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

        var reading = routes.MapGroup("/api/panel").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ReadAsync);
        reading.MapGet("/names", NamesAsync);

        var writing = routes.MapGroup("/api/panel").RequireScope(Scopes.ManageAccess);
        writing.MapPut("/", SaveAsync);
        writing.MapPost("/open", OpenAsync);
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

    /// <summary>
    /// Stops the panel a moment after the answer leaves, so systemd, or compose in a container, starts it again.
    /// </summary>
    internal static void StartOver(IHostApplicationLifetime life)
    {
        ArgumentNullException.ThrowIfNull(life);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
            life.StopApplication();
        });
    }

    private static async Task<IResult> ReadAsync(
        PanelStore store,
        ConfigStore configs,
        PanelSettings running,
        PanelPlace answering,
        WebOptions options,
        CancellationToken ct)
    {
        var settings = await store.ReadAsync(ct).ConfigureAwait(false);
        var secure = await SecureAsync(settings, configs, options, ct).ConfigureAwait(false);

        return Results.Ok(PanelAnswers.Panel(settings, running, options, secure, answering));
    }

    private static async Task<IResult> NamesAsync(
        HttpContext context,
        ClientStore clients,
        ConfigStore configs,
        CancellationToken ct)
    {
        var endpoints = await configs.ListAsync(ct).ConfigureAwait(false);
        var members = await clients.ListAsync(ct).ConfigureAwait(false);
        var first = members
            .OrderBy(one => one.Id)
            .Select(one => (Client: one, Endpoint: endpoints.FirstOrDefault(endpoint => endpoint.Id == one.ConfigId)))
            .FirstOrDefault(pair => pair.Endpoint is not null);

        return first.Endpoint is null
            ? Results.Ok(new NameSample(new Dictionary<string, string>(), string.Empty))
            : Results.Ok(new NameSample(
                ClientText.Values(first.Endpoint, first.Client, context.Request.Host.Host),
                ClientText.Stamp(first.Client)));
    }

    private static async Task<IResult> SaveAsync(
        PanelRequest request,
        PanelStore store,
        ConfigStore configs,
        PanelSettings running,
        PanelPlace answering,
        WebOptions options,
        FirewallApplier firewall,
        UpdateCenter updates,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var before = await store.ReadAsync(ct).ConfigureAwait(false);
        var draft = PanelAnswers.Draft(request, before);
        var logger = loggers.CreateLogger(typeof(PanelEndpoints));
        var fault = PanelRules.Check(draft) is null
            ? CertificateFiles.Check(draft.Certificate, draft.CertificateKey, logger)
            : null;
        if (fault is not null)
        {
            return Results.Json(new Failure(fault.Code, fault.Message), statusCode: StatusCodes.Status400BadRequest);
        }

        if (await configs.SocketUnderAsync(draft.Port, draft.Path, ct).ConfigureAwait(false) is { } socket)
        {
            return Results.Json(
                new Failure(
                    "panel-path-taken",
                    $"the websocket of {socket.Name} comes under '/{ConfigServices.WebSocketPath(socket)}' on TCP port {draft.Port}"),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var previous = request.Keep == true ? await firewall.PlanAsync(ct).ConfigureAwait(false) : null;
        var result = await store.SaveAsync(draft, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Record is null)
        {
            return Results.Json(new Failure(result.Code, result.Message), statusCode: StatusCodes.Status400BadRequest);
        }

        await firewall.SettleAsync(previous, ct).ConfigureAwait(false);
        if (result.Record.Prereleases != before.Prereleases)
        {
            updates.Recheck();
        }

        var secure = await SecureAsync(result.Record, configs, options, ct).ConfigureAwait(false);

        return Results.Ok(PanelAnswers.Panel(result.Record, running, options, secure, answering));
    }

    // Holds the port of the panel open in the firewall of the host at once, answering as a save does.
    private static async Task<IResult> OpenAsync(
        PanelStore store,
        ConfigStore configs,
        PanelSettings running,
        PanelPlace answering,
        WebOptions options,
        FirewallApplier firewall,
        CancellationToken ct)
    {
        var held = await store.ReadAsync(ct).ConfigureAwait(false);
        var result = await store.SaveAsync(held with { Opened = true }, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Record is null)
        {
            return Results.Json(new Failure(result.Code, result.Message), statusCode: StatusCodes.Status400BadRequest);
        }

        var sync = await firewall.SettleAsync(ct).ConfigureAwait(false);
        if (!sync.IsDone)
        {
            return Results.Json(new Failure("firewall-refused", sync.Message), statusCode: StatusCodes.Status409Conflict);
        }

        var secure = await SecureAsync(result.Record, configs, options, ct).ConfigureAwait(false);

        return Results.Ok(PanelAnswers.Panel(result.Record, running, options, secure, answering));
    }

    // Tells whether the panel speaks TLS once it starts under the settings: under their certificate, or under one made
    // up on a port it shares with the services.
    private static async Task<bool> SecureAsync(PanelSettings settings, ConfigStore configs, WebOptions options, CancellationToken ct) =>
        Listening.Chain(options, settings).Length > 0 || await configs.ServesAsync(settings.Port, ct).ConfigureAwait(false);

    private static IResult Restart(IHostApplicationLifetime life, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(PanelEndpoints));
        logger.LogInformation("the panel was told to start over");
        StartOver(life);

        return Results.Accepted();
    }
}
