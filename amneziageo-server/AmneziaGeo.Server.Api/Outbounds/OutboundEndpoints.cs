using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Api.Outbounds;

/// <summary>
/// The routes the ways out of the host are managed through.
/// </summary>
public static class OutboundEndpoints
{
    /// <summary>
    /// Maps the outbound management routes.
    /// </summary>
    public static IEndpointRouteBuilder MapOutbounds(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var reading = routes.MapGroup("/api/outbounds").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/{id:long}", FindAsync);

        var writing = routes.MapGroup("/api/outbounds").RequireScope(Scopes.ManageRouting);
        writing.MapGet("/draft", Draft);
        writing.MapPost("/keys", Keys);
        writing.MapPost("/import", Import);
        writing.MapPost("/", AddAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapPost("/{id:long}/switch", SwitchAsync);
        writing.MapPost("/{id:long}/move", MoveAsync);
        writing.MapPost("/{id:long}/apply", ApplyAsync);
        writing.MapPost("/apply", SyncAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        OutboundStore store,
        OutboundHost host,
        CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var states = host.States(found);
        var secrets = Secrets(context);

        return Results.Ok(found
            .Select((outbound, at) => OutboundAnswers.Outbound(outbound, states[at], secrets))
            .ToArray());
    }

    private static async Task<IResult> FindAsync(
        long id,
        HttpContext context,
        OutboundStore store,
        OutboundHost host,
        CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);

        return found is null
            ? Refuse(StatusCodes.Status404NotFound, "unknown-outbound", $"there is no outbound under the number {id}")
            : Results.Ok(OutboundAnswers.Outbound(found, host.State(found), Secrets(context)));
    }

    private static IResult Draft(string? name, string? kind) =>
        Results.Ok(OutboundAnswers.Outbound(
            OutboundDefaults.Fresh(
                string.IsNullOrWhiteSpace(name) ? "out0" : name.Trim(),
                string.IsNullOrWhiteSpace(kind) ? OutboundKind.Wg : kind.Trim()),
            null,
            true));

    private static IResult Keys()
    {
        var pair = Curve25519.Create();

        return Results.Ok(new KeyPairResponse(pair.PrivateKey, pair.PublicKey));
    }

    private static IResult Import(OutboundImportRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var result = OutboundImport.Read(request.Config, name.Length > 0 ? name : "out0");

        return result.Outbound is null
            ? Refuse(StatusCodes.Status400BadRequest, result.Fault!.Code, result.Fault.Message)
            : Results.Ok(OutboundAnswers.Outbound(result.Outbound, null, true));
    }

    private static async Task<IResult> AddAsync(
        OutboundRequest request,
        OutboundStore store,
        OutboundHost host,
        RouteApplier routes,
        CancellationToken ct)
    {
        var result = await store.AddAsync(OutboundAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var state = await host.ApplyAsync(result.Record!, ct).ConfigureAwait(false);
        await FirewallAsync(store, host, routes, ct).ConfigureAwait(false);

        return Results.Created(
            $"/api/outbounds/{result.Record!.Id}",
            OutboundAnswers.Outbound(result.Record, state, true));
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        OutboundRequest request,
        OutboundStore store,
        OutboundHost host,
        RouteApplier routes,
        CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        var result = await store.ChangeAsync(id, OutboundAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        if (held is not null && held.Name != result.Record!.Name)
        {
            await host.WithdrawAsync(held, ct).ConfigureAwait(false);
        }

        var state = await host.ApplyAsync(result.Record!, ct).ConfigureAwait(false);
        await FirewallAsync(store, host, routes, ct).ConfigureAwait(false);

        return Results.Ok(OutboundAnswers.Outbound(result.Record!, state, true));
    }

    private static async Task<IResult> SwitchAsync(
        long id,
        OutboundSwitchRequest request,
        OutboundStore store,
        OutboundHost host,
        RouteApplier routes,
        CancellationToken ct)
    {
        var result = await store.SwitchAsync(id, request.On, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var state = await host.ApplyAsync(result.Record!, ct).ConfigureAwait(false);
        await FirewallAsync(store, host, routes, ct).ConfigureAwait(false);

        return Results.Ok(OutboundAnswers.Outbound(result.Record!, state, true));
    }

    private static async Task<IResult> MoveAsync(
        long id,
        OutboundMoveRequest request,
        OutboundStore store,
        OutboundHost host,
        CancellationToken ct)
    {
        var result = await store.MoveAsync(id, request.Up, ct).ConfigureAwait(false);

        return result.IsOk
            ? Results.Ok(OutboundAnswers.Outbound(result.Record!, host.State(result.Record!), true))
            : Explain(result);
    }

    private static async Task<IResult> RemoveAsync(
        long id,
        OutboundStore store,
        OutboundHost host,
        RouteApplier routes,
        CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await host.WithdrawAsync(result.Record!, ct).ConfigureAwait(false);
        await FirewallAsync(store, host, routes, ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult> ApplyAsync(long id, OutboundStore store, OutboundHost host, RouteApplier routes, CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-outbound", $"there is no outbound under the number {id}");
        }

        var state = await host.ApplyAsync(found, ct).ConfigureAwait(false);
        await FirewallAsync(store, host, routes, ct).ConfigureAwait(false);

        return Results.Ok(OutboundAnswers.Outbound(found, state, true));
    }

    private static async Task<IResult> SyncAsync(OutboundStore store, OutboundHost host, RouteApplier routes, CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        try
        {
            var states = await host.SyncAsync(found, ct).ConfigureAwait(false);
            await routes.SettleAsync(ct).ConfigureAwait(false);

            return Results.Ok(found
                .Select((outbound, at) => OutboundAnswers.Outbound(outbound, states[at], true))
                .ToArray());
        }
        catch (HostNetworkException ex)
        {
            return Refuse(StatusCodes.Status409Conflict, "host-refused", ex.Message);
        }
    }

    private static async Task FirewallAsync(OutboundStore store, OutboundHost host, RouteApplier routes, CancellationToken ct)
    {
        try
        {
            await host.FirewallAsync(await store.ListAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        }
        catch (HostNetworkException)
        {
        }

        await routes.SettleAsync(ct).ConfigureAwait(false);
    }

    private static bool Secrets(HttpContext context) => context.Caller()?.Holds(Scopes.ManageRouting) == true;

    private static IResult Explain(OutboundResult result) => Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(OutboundOutcome outcome) => outcome switch
    {
        OutboundOutcome.Unknown => StatusCodes.Status404NotFound,
        OutboundOutcome.NameTaken or OutboundOutcome.NoMark => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
