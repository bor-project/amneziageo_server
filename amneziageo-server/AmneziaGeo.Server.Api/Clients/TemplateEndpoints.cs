using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// The routes the client templates are managed through.
/// </summary>
public static class TemplateEndpoints
{
    /// <summary>
    /// Maps the client template routes.
    /// </summary>
    public static IEndpointRouteBuilder MapTemplates(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var reading = routes.MapGroup("/api/templates").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/defaults", DefaultsAsync);
        reading.MapGet("/{id:long}", FindAsync);

        var writing = routes.MapGroup("/api/templates").RequireScope(Scopes.ManageClients);
        writing.MapPost("/", AddAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapPost("/{id:long}/refresh", RefreshAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(TemplateStore store, CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var uses = await store.UsesAsync(ct).ConfigureAwait(false);

        return Results.Ok(found.Select(one => TemplateAnswers.Template(one, uses.GetValueOrDefault(one.Id))).ToArray());
    }

    private static async Task<IResult> FindAsync(long id, TemplateStore store, CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Missing(id);
        }

        var uses = await store.UsesAsync(ct).ConfigureAwait(false);

        return Results.Ok(TemplateAnswers.Template(found, uses.GetValueOrDefault(found.Id)));
    }

    private static async Task<IResult> DefaultsAsync(ConfigStore configs, CancellationToken ct)
    {
        var found = await configs.ListAsync(ct).ConfigureAwait(false);

        return Results.Ok(TemplateAnswers.Defaults(found.SelectMany(config => config.Address)));
    }

    private static async Task<IResult> AddAsync(
        TemplateRequest request,
        TemplateStore store,
        TemplateRefresher refresher,
        CancellationToken ct)
    {
        var draft = TemplateAnswers.Draft(request);
        if (await store.RefuseAsync(draft, 0, ct).ConfigureAwait(false) is { } refusal)
        {
            return Explain(refusal);
        }

        var full = await refresher.ResolveAsync(draft, ct).ConfigureAwait(false);
        var result = await store.AddAsync(full, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        return Results.Created($"/api/templates/{result.Record!.Id}", TemplateAnswers.Template(result.Record, 0));
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        TemplateRequest request,
        TemplateStore store,
        TemplateRefresher refresher,
        CancellationToken ct)
    {
        if (await store.FindAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Missing(id);
        }

        var draft = TemplateAnswers.Draft(request);
        if (await store.RefuseAsync(draft, id, ct).ConfigureAwait(false) is { } refusal)
        {
            return Explain(refusal);
        }

        var full = await refresher.ResolveAsync(draft, ct).ConfigureAwait(false);

        return await KeepAsync(id, full, store, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> RefreshAsync(
        long id,
        TemplateStore store,
        TemplateRefresher refresher,
        CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Missing(id);
        }

        var full = await refresher.ResolveAsync(held, ct).ConfigureAwait(false);

        return await KeepAsync(id, full, store, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> RemoveAsync(long id, TemplateStore store, CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);

        return result.IsOk ? Results.NoContent() : Explain(result);
    }

    private static async Task<IResult> KeepAsync(
        long id,
        ClientTemplate template,
        TemplateStore store,
        CancellationToken ct)
    {
        var result = await store.ChangeAsync(id, template, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var uses = await store.UsesAsync(ct).ConfigureAwait(false);

        return Results.Ok(TemplateAnswers.Template(result.Record!, uses.GetValueOrDefault(id)));
    }

    private static IResult Missing(long id) =>
        Refuse(StatusCodes.Status404NotFound, "unknown-template", $"there is no template under the number {id}");

    private static IResult Explain(TemplateResult result) => Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(TemplateOutcome outcome) => outcome switch
    {
        TemplateOutcome.Unknown => StatusCodes.Status404NotFound,
        TemplateOutcome.NameTaken or TemplateOutcome.InUse => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
