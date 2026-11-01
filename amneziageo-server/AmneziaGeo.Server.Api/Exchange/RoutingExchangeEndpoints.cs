using System.Text;

using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Dal.Exchange;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Exchange;

/// <summary>
/// The routes the routing of the server travels to another server through: a file out, a file in over a plan.
/// </summary>
public static class RoutingExchangeEndpoints
{
    /// <summary>
    /// Maps the routes of the exchange.
    /// </summary>
    public static IEndpointRouteBuilder MapRoutingExchange(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGroup("/api/routing").RequireScope(Scopes.ReadState).MapGet("/export", ExportAsync);

        var writing = routes.MapGroup("/api/routing").RequireScope(Scopes.ManageRouting);
        writing.MapPost("/import", ImportAsync);
        writing.MapPost("/import/apply", ApplyAsync);

        return routes;
    }

    private static async Task<IResult> ExportAsync(
        HttpContext context,
        RouteStore rules,
        PresetStore presets,
        GeoStore sources,
        IAuditLog audit,
        CancellationToken ct)
    {
        if (context.Caller() is not { } caller)
        {
            return Results.Unauthorized();
        }

        var doc = await RoutingExport.TakeAsync(rules, presets, sources, ct).ConfigureAwait(false);
        await audit.WriteAsync(DateTimeOffset.UtcNow, caller.Id, caller.Scheme, "routing.export", null, null, context.Address(), ct)
            .ConfigureAwait(false);

        return Results.File(
            Encoding.UTF8.GetBytes(RoutingExport.Text(doc)),
            "application/json",
            Name(context.Request.Host.Host, DateTimeOffset.UtcNow));
    }

    private static async Task<IResult> ImportAsync(
        HttpContext context,
        RouteStore rules,
        PresetStore presets,
        GeoStore sources,
        OutboundStore outbounds,
        ClientStore clients,
        CancellationToken ct)
    {
        if (context.Caller() is null)
        {
            return Results.Unauthorized();
        }

        var read = await Taken(context, ct).ConfigureAwait(false);
        if (!read.IsOk)
        {
            return Refuse(read);
        }

        var plan = await RoutingImport.PlanAsync(read.Doc!, rules, presets, sources, outbounds, clients, ct).ConfigureAwait(false);

        return Results.Ok(plan);
    }

    private static async Task<IResult> ApplyAsync(
        HttpContext context,
        RouteStore rules,
        PresetStore presets,
        GeoStore sources,
        OutboundStore outbounds,
        ClientStore clients,
        RouteApplier applier,
        IAuditLog audit,
        CancellationToken ct)
    {
        if (context.Caller() is not { } caller)
        {
            return Results.Unauthorized();
        }

        var read = await Taken(context, ct).ConfigureAwait(false);
        if (!read.IsOk)
        {
            return Refuse(read);
        }

        var plan = await RoutingImport.PlanAsync(read.Doc!, rules, presets, sources, outbounds, clients, ct).ConfigureAwait(false);
        var answer = await RoutingImport.ApplyAsync(plan, rules, presets, sources, ct).ConfigureAwait(false);

        try
        {
            await applier.SettleAsync(ct).ConfigureAwait(false);
        }
        catch (HostNetworkException ex)
        {
            return Results.Json(new Failure("host-refused", ex.Message), statusCode: StatusCodes.Status409Conflict);
        }

        await audit.WriteAsync(DateTimeOffset.UtcNow, caller.Id, caller.Scheme, "routing.import", null, null, context.Address(), ct)
            .ConfigureAwait(false);

        return Results.Ok(answer);
    }

    private static async Task<RoutingRead> Taken(HttpContext context, CancellationToken ct)
    {
        using var reader = new StreamReader(context.Request.Body);
        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        return RoutingFile.Read(text);
    }

    private static IResult Refuse(RoutingRead read) =>
        Results.Json(new Failure(read.Code, read.Message), statusCode: StatusCodes.Status400BadRequest);

    private static string Name(string host, DateTimeOffset now)
    {
        var clean = new string(host.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-').ToArray());

        return clean.Length > 0
            ? $"amneziageo-routing-{clean}-{now:yyyyMMdd-HHmmss}.json"
            : $"amneziageo-routing-{now:yyyyMMdd-HHmmss}.json";
    }
}
