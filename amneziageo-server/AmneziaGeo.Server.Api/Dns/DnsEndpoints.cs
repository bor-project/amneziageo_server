using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Names;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// The routes the resolver is managed through.
/// </summary>
public static class DnsEndpoints
{
    /// <summary>
    /// Maps the resolver routes.
    /// </summary>
    public static IEndpointRouteBuilder MapResolver(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var reading = routes.MapGroup("/api/dns").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ReadAsync);
        reading.MapGet("/conflicts", DnsChecks.ConflictsAsync);
        reading.MapPost("/check", DnsChecks.CheckAsync);

        var writing = routes.MapGroup("/api/dns").RequireScope(Scopes.ManageRouting);
        writing.MapPut("/", SaveAsync);
        writing.MapPost("/restart", RestartAsync);

        return routes;
    }

    private static async Task<IResult> ReadAsync(
        DnsStore store,
        DnsState state,
        DnsSets sets,
        RoutePlans plans,
        NameState names,
        ConfigStore configs,
        ResolverDoors doors,
        CancellationToken ct)
    {
        var settings = await store.ReadAsync(ct).ConfigureAwait(false);
        var running = state.Settings ?? settings with { IsEnabled = false };
        var shut = await doors.KeptOutAsync(running, await configs.ListAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);

        return Results.Ok(DnsAnswers.Resolver(settings, state, sets, plans.Held, names.Fault, names.Counts, shut));
    }

    private static async Task<IResult> SaveAsync(
        DnsRequest request,
        DnsStore store,
        OutboundStore outbounds,
        BalanceStore balancers,
        DnsState state,
        DnsSets sets,
        RoutePlans plans,
        NameState names,
        CancellationToken ct)
    {
        var draft = DnsAnswers.Draft(request);
        if (draft.Outbound.Length > 0
            && !DnsExit.Knows(
                draft.Outbound,
                await outbounds.ListAsync(ct).ConfigureAwait(false),
                await balancers.ListAsync(ct).ConfigureAwait(false)))
        {
            var missing = new Failure(
                "unknown-outbound",
                $"there is no outbound or balancer called '{draft.Outbound}'");

            return Results.Json(missing, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await store.SaveAsync(draft, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Record is null)
        {
            return Results.Json(new Failure(result.Code, result.Message), statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(DnsAnswers.Resolver(result.Record, state, sets, plans.Held, names.Fault, names.Counts));
    }

    private static async Task<IResult> RestartAsync(
        DnsStore store,
        DnsHost host,
        RouteApplier applier,
        FirewallApplier firewall,
        DnsState state,
        DnsSets sets,
        NameState names,
        CancellationToken ct)
    {
        await host.RestartAsync(ct).ConfigureAwait(false);
        var plan = await applier.SettleAsync(ct).ConfigureAwait(false);
        await firewall.SettleAsync(ct).ConfigureAwait(false);
        var settings = await store.ReadAsync(ct).ConfigureAwait(false);

        return Results.Ok(DnsAnswers.Resolver(settings, state, sets, plan, names.Fault, names.Counts));
    }
}
