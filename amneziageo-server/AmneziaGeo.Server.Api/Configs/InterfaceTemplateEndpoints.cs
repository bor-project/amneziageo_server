using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// The routes the endpoint templates are managed through.
/// </summary>
public static class InterfaceTemplateEndpoints
{
    /// <summary>
    /// Maps the endpoint template routes.
    /// </summary>
    public static IEndpointRouteBuilder MapInterfaceTemplates(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var reading = routes.MapGroup("/api/templates/interfaces").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/{id:long}", FindAsync);

        var writing = routes.MapGroup("/api/templates/interfaces").RequireScope(Scopes.ManageInterfaces);
        writing.MapGet("/draft", Draft);
        writing.MapPost("/", AddAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(InterfaceTemplateStore store, CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var uses = await store.UsesAsync(ct).ConfigureAwait(false);

        return Results.Ok(found
            .Select(one => InterfaceTemplateAnswers.Template(one, uses.GetValueOrDefault(one.Id)))
            .ToArray());
    }

    private static async Task<IResult> FindAsync(long id, InterfaceTemplateStore store, CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Missing(id);
        }

        var uses = await store.UsesAsync(ct).ConfigureAwait(false);

        return Results.Ok(InterfaceTemplateAnswers.Template(found, uses.GetValueOrDefault(found.Id)));
    }

    private static IResult Draft() =>
        Results.Ok(InterfaceTemplateAnswers.Template(InterfaceTemplateDefaults.Fresh(), 0));

    private static async Task<IResult> AddAsync(
        InterfaceTemplateRequest request,
        InterfaceTemplateStore store,
        CancellationToken ct)
    {
        var result = await store.AddAsync(InterfaceTemplateAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        return Results.Created(
            $"/api/templates/interfaces/{result.Record!.Id}",
            InterfaceTemplateAnswers.Template(result.Record, 0));
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        InterfaceTemplateRequest request,
        InterfaceTemplateStore store,
        TemplateSpread spread,
        CancellationToken ct)
    {
        var result = await store.ChangeAsync(id, InterfaceTemplateAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var done = await spread.InterfacesAsync(result.Record!, ct).ConfigureAwait(false);
        var uses = await store.UsesAsync(ct).ConfigureAwait(false);

        return Results.Ok(new InterfaceTemplateSaveResponse(
            InterfaceTemplateAnswers.Template(result.Record!, uses.GetValueOrDefault(id)),
            [.. done.Select(one => new ConfigSyncResponse(one.Name, one.IsDone, one.Message))]));
    }

    private static async Task<IResult> RemoveAsync(long id, InterfaceTemplateStore store, CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);

        return result.IsOk ? Results.NoContent() : Explain(result);
    }

    private static IResult Missing(long id) => Refuse(
        StatusCodes.Status404NotFound,
        "unknown-template",
        $"there is no endpoint template under the number {id}");

    private static IResult Explain(InterfaceTemplateResult result) =>
        Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(TemplateOutcome outcome) => outcome switch
    {
        TemplateOutcome.Unknown => StatusCodes.Status404NotFound,
        TemplateOutcome.NameTaken or TemplateOutcome.InUse => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
