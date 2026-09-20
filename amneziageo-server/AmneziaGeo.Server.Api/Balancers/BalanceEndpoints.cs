using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Api.Balancers;

/// <summary>
/// The routes the balancers are managed through.
/// </summary>
public static class BalanceEndpoints
{
    /// <summary>
    /// Maps the balancer management routes.
    /// </summary>
    public static IEndpointRouteBuilder MapBalancers(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var reading = routes.MapGroup("/api/balancers").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/{id:long}", FindAsync);

        var writing = routes.MapGroup("/api/balancers").RequireScope(Scopes.ManageRouting);
        writing.MapGet("/draft", Draft);
        writing.MapPost("/", AddAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapPost("/{id:long}/switch", SwitchAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        BalanceStore store,
        OutboundStore outbounds,
        OutboundHost host,
        CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var ways = await outbounds.ListAsync(ct).ConfigureAwait(false);
        var states = host.States(ways);

        return Results.Ok(found.Select(one => BalanceAnswers.Balancer(one, ways, states)).ToArray());
    }

    private static async Task<IResult> FindAsync(
        long id,
        BalanceStore store,
        OutboundStore outbounds,
        OutboundHost host,
        CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Missing(id);
        }

        var ways = await outbounds.ListAsync(ct).ConfigureAwait(false);

        return Results.Ok(BalanceAnswers.Balancer(found, ways, host.States(ways)));
    }

    private static IResult Draft(string? name) =>
        Results.Ok(BalanceAnswers.Balancer(
            BalanceDefaults.Fresh(string.IsNullOrWhiteSpace(name) ? "balancer" : name.Trim()),
            [],
            []));

    private static async Task<IResult> AddAsync(
        BalanceRequest request,
        BalanceStore store,
        OutboundStore outbounds,
        OutboundHost host,
        RouteApplier applier,
        CancellationToken ct)
    {
        var result = await store.AddAsync(BalanceAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var body = await AnswerAsync(result.Record!, outbounds, host, applier, ct).ConfigureAwait(false);

        return Results.Created($"/api/balancers/{result.Record!.Id}", body);
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        BalanceRequest request,
        BalanceStore store,
        OutboundStore outbounds,
        OutboundHost host,
        RouteApplier applier,
        DnsHost resolver,
        CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        var result = await store.ChangeAsync(id, BalanceAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var body = await AnswerAsync(result.Record!, outbounds, host, applier, ct).ConfigureAwait(false);
        if (held is not null)
        {
            await resolver.FollowAsync(held.Name, result.Record!.Name, ct).ConfigureAwait(false);
        }

        return Results.Ok(body);
    }

    private static async Task<IResult> SwitchAsync(
        long id,
        BalanceSwitchRequest request,
        BalanceStore store,
        OutboundStore outbounds,
        OutboundHost host,
        RouteApplier applier,
        CancellationToken ct)
    {
        if (request.On is not { } on)
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "a switch needs the on field");
        }

        var result = await store.SwitchAsync(id, on, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        return Results.Ok(await AnswerAsync(result.Record!, outbounds, host, applier, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> RemoveAsync(
        long id,
        BalanceStore store,
        RouteApplier applier,
        DnsHost resolver,
        CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, resolver.Asking, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await applier.SettleAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<BalanceResponse> AnswerAsync(
        Balancer balancer,
        OutboundStore outbounds,
        OutboundHost host,
        RouteApplier applier,
        CancellationToken ct)
    {
        await applier.SettleAsync(ct).ConfigureAwait(false);
        var ways = await outbounds.ListAsync(ct).ConfigureAwait(false);

        return BalanceAnswers.Balancer(balancer, ways, host.States(ways));
    }

    private static IResult Missing(long id) =>
        Refuse(StatusCodes.Status404NotFound, "unknown-balancer", $"there is no balancer under the number {id}");

    private static IResult Explain(BalanceResult result) =>
        Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(BalanceOutcome outcome) => outcome switch
    {
        BalanceOutcome.Unknown => StatusCodes.Status404NotFound,
        BalanceOutcome.NameTaken => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
