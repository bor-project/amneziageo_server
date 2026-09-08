using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Geo.Files;

namespace AmneziaGeo.Server.Api.Geo;

/// <summary>
/// The routes the geo databases are managed through.
/// </summary>
public static class GeoEndpoints
{
    /// <summary>
    /// Maps the geo routes.
    /// </summary>
    public static IEndpointRouteBuilder MapGeo(this IEndpointRouteBuilder routes)
    {
        var reading = routes.MapGroup("/api/geo").RequireScope(Scopes.ReadState);
        reading.MapGet("/sources", ListAsync);
        reading.MapGet("/keys", KeysAsync);

        var writing = routes.MapGroup("/api/geo").RequireScope(Scopes.ManageRouting);
        writing.MapPost("/sources", AddAsync);
        writing.MapPut("/sources/{id:long}", ChangeAsync);
        writing.MapDelete("/sources/{id:long}", RemoveAsync);
        writing.MapPost("/sources/{id:long}/move", MoveAsync);
        writing.MapPost("/sources/{id:long}/update", UpdateAsync);
        writing.MapPost("/update", UpdateAllAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(GeoStore store, CancellationToken ct)
    {
        var held = await store.ListAsync(ct).ConfigureAwait(false);

        return Results.Ok(held.Select(GeoAnswers.Source).ToArray());
    }

    private static async Task<IResult> KeysAsync(GeoStore store, IGeoFileStore files, CancellationToken ct)
    {
        var held = await store.ListAsync(ct).ConfigureAwait(false);
        var index = GeoIndex.Load(held, files);

        return Results.Ok(new GeoKeysResponse([.. index.Countries()], [.. index.Categories()]));
    }

    private static async Task<IResult> AddAsync(
        GeoSourceRequest request,
        GeoStore store,
        RouteApplier routes,
        CancellationToken ct)
    {
        var result = await store.AddAsync(GeoAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await routes.SettleAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/geo/sources/{result.Record!.Id}", GeoAnswers.Source(result.Record));
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        GeoSourceRequest request,
        GeoStore store,
        RouteApplier routes,
        CancellationToken ct)
    {
        var result = await store.ChangeAsync(id, GeoAnswers.Draft(request), ct).ConfigureAwait(false);

        return await SettleAsync(result, routes, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> RemoveAsync(long id, GeoStore store, RouteApplier routes, CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await routes.SettleAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult> MoveAsync(
        long id,
        GeoMoveRequest request,
        GeoStore store,
        RouteApplier routes,
        CancellationToken ct)
    {
        var result = await store.MoveAsync(id, request.Up, ct).ConfigureAwait(false);

        return await SettleAsync(result, routes, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> UpdateAsync(
        long id,
        GeoStore store,
        GeoRefresher refresher,
        RouteApplier routes,
        CancellationToken ct)
    {
        var source = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (source is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-source", $"there is no geo source under the number {id}");
        }

        var fresh = await refresher.RefreshAsync(source, ct).ConfigureAwait(false);
        await routes.SettleAsync(ct).ConfigureAwait(false);

        return Results.Ok(GeoAnswers.Source(fresh));
    }

    private static async Task<IResult> UpdateAllAsync(
        GeoRefresher refresher,
        RouteApplier routes,
        TimeProvider time,
        CancellationToken ct)
    {
        var fresh = await refresher.RefreshAllAsync(null, time.GetUtcNow(), ct).ConfigureAwait(false);
        await routes.SettleAsync(ct).ConfigureAwait(false);

        return Results.Ok(fresh.Select(GeoAnswers.Source).ToArray());
    }

    private static async Task<IResult> SettleAsync(GeoResult result, RouteApplier routes, CancellationToken ct)
    {
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await routes.SettleAsync(ct).ConfigureAwait(false);

        return Results.Ok(GeoAnswers.Source(result.Record!));
    }

    private static IResult Explain(GeoResult result) => Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(GeoOutcome outcome) => outcome switch
    {
        GeoOutcome.Unknown => StatusCodes.Status404NotFound,
        GeoOutcome.NameTaken or GeoOutcome.UrlTaken => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
