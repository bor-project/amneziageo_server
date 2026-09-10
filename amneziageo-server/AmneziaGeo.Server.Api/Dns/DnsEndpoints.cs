using AmneziaGeo.Server.Api.Auth;
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

        routes.MapGroup("/api/dns").RequireScope(Scopes.ReadState).MapGet("/", ReadAsync);

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
        CancellationToken ct)
    {
        var settings = await store.ReadAsync(ct).ConfigureAwait(false);

        return Results.Ok(DnsAnswers.Resolver(settings, state, sets, plans.Held));
    }

    private static async Task<IResult> SaveAsync(
        DnsRequest request,
        DnsStore store,
        DnsState state,
        DnsSets sets,
        RoutePlans plans,
        CancellationToken ct)
    {
        var result = await store.SaveAsync(DnsAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk || result.Record is null)
        {
            return Results.Json(new Failure(result.Code, result.Message), statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(DnsAnswers.Resolver(result.Record, state, sets, plans.Held));
    }

    private static async Task<IResult> RestartAsync(
        DnsStore store,
        DnsHost host,
        RouteApplier applier,
        DnsState state,
        DnsSets sets,
        CancellationToken ct)
    {
        await host.RestartAsync(ct).ConfigureAwait(false);
        var plan = await applier.SettleAsync(ct).ConfigureAwait(false);
        var settings = await store.ReadAsync(ct).ConfigureAwait(false);

        return Results.Ok(DnsAnswers.Resolver(settings, state, sets, plan));
    }
}
