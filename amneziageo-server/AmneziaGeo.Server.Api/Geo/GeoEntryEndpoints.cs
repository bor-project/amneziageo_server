using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Geo.Files;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Geo;

/// <summary>
/// What a geo key carries, as the interface reads it.
/// </summary>
public sealed record GeoEntriesResponse(int Total, IReadOnlyList<string> Entries);

/// <summary>
/// The route that shows what a geo key carries.
/// </summary>
public static class GeoEntryEndpoints
{
    /// <summary>
    /// The most entries one answer carries.
    /// </summary>
    public const int MaxShown = 1000;

    /// <summary>
    /// Maps the route of the entries of a geo key.
    /// </summary>
    public static IEndpointRouteBuilder MapGeoEntries(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGroup("/api/geo").RequireScope(Scopes.ReadState).MapGet("/entries", EntriesAsync);

        return routes;
    }

    private static async Task<IResult> EntriesAsync(
        string? key,
        int? limit,
        GeoStore store,
        IGeoFileStore files,
        CancellationToken ct)
    {
        var rule = RouteRules.Target(key);
        if (rule is not { Kind: GeoRuleKind.GeoIp or GeoRuleKind.GeoSite })
        {
            return Results.Json(
                new Failure("bad-key", $"'{key}' is not a geo key"),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var held = await store.ListAsync(ct).ConfigureAwait(false);
        var found = Entries(rule, GeoIndex.Load(held, files));

        return Results.Ok(new GeoEntriesResponse(found.Count, [.. found.Take(Math.Clamp(limit ?? 200, 1, MaxShown))]));
    }

    private static IReadOnlyList<string> Entries(GeoRule rule, GeoIndex index)
    {
        if (rule.Kind == GeoRuleKind.GeoIp)
        {
            return index.Cidrs(rule.Value);
        }

        return [.. index.Domains(rule.Value).Select(Written)];
    }

    private static string Written(GeoDomain domain) => domain.Kind switch
    {
        GeoDomainKind.Full => "full:" + domain.Value,
        GeoDomainKind.Plain => "keyword:" + domain.Value,
        GeoDomainKind.Regex => "regexp:" + domain.Value,
        _ => domain.Value,
    };
}
