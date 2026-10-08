using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// The routes the routing presets are managed through.
/// </summary>
public static class PresetEndpoints
{
    /// <summary>
    /// The address the routing presets sit under.
    /// </summary>
    public const string Prefix = "/api/templates/routing";

    /// <summary>
    /// Maps the routing preset routes.
    /// </summary>
    public static IEndpointRouteBuilder MapPresets(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var reading = routes.MapGroup(Prefix).RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/{id:long}", FindAsync);

        var writing = routes.MapGroup(Prefix).RequireScope(Scopes.ManageClients);
        writing.MapPost("/", AddAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(PresetStore store, CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var uses = await store.UsesAsync(ct).ConfigureAwait(false);

        return Results.Ok(found.Select(one => PresetAnswers.Preset(one, uses.GetValueOrDefault(one.Id))).ToArray());
    }

    private static async Task<IResult> FindAsync(long id, PresetStore store, CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Missing(id);
        }

        var uses = await store.UsesAsync(ct).ConfigureAwait(false);

        return Results.Ok(PresetAnswers.Preset(found, uses.GetValueOrDefault(found.Id)));
    }

    private static async Task<IResult> AddAsync(PresetRequest request, PresetStore store, CancellationToken ct)
    {
        var result = await store.AddAsync(PresetAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        return Results.Created($"{Prefix}/{result.Record!.Id}", PresetAnswers.Preset(result.Record, 0));
    }

    private static async Task<IResult> ChangeAsync(long id, PresetRequest request, PresetStore store, CancellationToken ct)
    {
        var held = request.IsDefault is null ? await store.FindAsync(id, ct).ConfigureAwait(false) : null;
        var result = await store.ChangeAsync(id, PresetAnswers.Draft(request, held), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var uses = await store.UsesAsync(ct).ConfigureAwait(false);

        return Results.Ok(PresetAnswers.Preset(result.Record!, uses.GetValueOrDefault(id)));
    }

    private static async Task<IResult> RemoveAsync(long id, PresetStore store, CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);

        return result.IsOk ? Results.NoContent() : Explain(result);
    }

    private static IResult Missing(long id) =>
        Refuse(StatusCodes.Status404NotFound, "unknown-preset", $"there is no routing preset under the number {id}");

    private static IResult Explain(PresetResult result) => Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(PresetOutcome outcome) => outcome switch
    {
        PresetOutcome.Unknown => StatusCodes.Status404NotFound,
        PresetOutcome.NameTaken or PresetOutcome.InUse => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
