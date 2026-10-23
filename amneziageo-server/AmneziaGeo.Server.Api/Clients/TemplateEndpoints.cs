using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Template;

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

        Map(routes, "/api/templates");
        Map(routes, "/api/templates/clients");

        return routes;
    }

    private static void Map(IEndpointRouteBuilder routes, string prefix)
    {
        var reading = routes.MapGroup(prefix).RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/defaults", DefaultsAsync);
        reading.MapGet("/{id:long}", FindAsync);

        var writing = routes.MapGroup(prefix).RequireScope(Scopes.ManageClients);
        writing.MapPost("/", AddAsync);
        writing.MapPost("/preview", PreviewAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapPut("/default/clients", FreeAsync);
        writing.MapPost("/{id:long}/refresh", RefreshAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);
    }

    private static async Task<IResult> ListAsync(TemplateStore store, CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var members = await store.MembersAsync(ct).ConfigureAwait(false);

        return Results.Ok(found.Select(one => TemplateAnswers.Template(one, Of(members, one.Id))).ToArray());
    }

    private static async Task<IResult> FindAsync(long id, TemplateStore store, CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Missing(id);
        }

        var members = await store.MembersAsync(ct).ConfigureAwait(false);

        return Results.Ok(TemplateAnswers.Template(found, Of(members, found.Id)));
    }

    private static async Task<IResult> DefaultsAsync(ConfigStore configs, TemplateStore store, CancellationToken ct)
    {
        var found = await configs.ListAsync(ct).ConfigureAwait(false);
        var plain = await store.PlainAsync(ct).ConfigureAwait(false);

        return Results.Ok(TemplateAnswers.Defaults(found.SelectMany(config => config.Address), plain));
    }

    private static async Task<IResult> AddAsync(
        TemplateRequest request,
        TemplateStore store,
        TemplateRefresher refresher,
        RouteApplier routes,
        CancellationToken ct)
    {
        var draft = TemplateAnswers.Draft(request);
        if (await store.RefuseAsync(draft, 0, ct).ConfigureAwait(false) is { } refusal)
        {
            return Explain(refusal);
        }

        var full = await refresher.ResolveAsync(draft, ct).ConfigureAwait(false);
        var result = await store.AddAsync(full, TemplateAnswers.Members(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await routes.FollowStandInsAsync(ct).ConfigureAwait(false);
        var members = await store.MembersAsync(ct).ConfigureAwait(false);

        return Results.Created(
            $"/api/templates/{result.Record!.Id}",
            TemplateAnswers.Template(result.Record, Of(members, result.Record.Id)));
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        TemplateRequest request,
        TemplateStore store,
        TemplateRefresher refresher,
        RouteApplier routes,
        CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Missing(id);
        }

        var draft = TemplateAnswers.Change(request, held);
        if (await store.RefuseAsync(draft, id, ct).ConfigureAwait(false) is { } refusal)
        {
            return Explain(refusal);
        }

        var full = await refresher.ResolveAsync(draft, ct).ConfigureAwait(false);

        return await KeepAsync(id, full, TemplateAnswers.Members(request), store, routes, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> FreeAsync(
        TemplateClientsRequest request,
        TemplateStore store,
        ConfigStore configs,
        RouteApplier routes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Clients is not { } clients)
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "the clients without a template need the clients field");
        }

        if (await store.FreeAsync(clients, ct).ConfigureAwait(false) is { } refusal)
        {
            return Explain(refusal);
        }

        await routes.FollowStandInsAsync(ct).ConfigureAwait(false);

        return await DefaultsAsync(configs, store, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> RefreshAsync(
        long id,
        TemplateStore store,
        TemplateRefresher refresher,
        RouteApplier routes,
        CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Missing(id);
        }

        var full = await refresher.ResolveAsync(held, ct).ConfigureAwait(false);

        return await KeepAsync(id, full, null, store, routes, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> PreviewAsync(
        TemplatePreviewRequest request,
        TemplateRefresher refresher,
        CancellationToken ct)
    {
        var entries = TemplateAnswers.Entries(request.Entries);
        if (TemplateList.Check(entries) is { } fault)
        {
            return Refuse(StatusCodes.Status400BadRequest, fault.Code, fault.Message);
        }

        var found = await refresher.FindAsync(entries, request.StandIns ?? false, ct).ConfigureAwait(false);

        return Results.Ok(TemplateAnswers.Preview(found));
    }

    private static async Task<IResult> RemoveAsync(long id, TemplateStore store, RouteApplier routes, CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await routes.FollowStandInsAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult> KeepAsync(
        long id,
        ClientTemplate template,
        IReadOnlyList<long>? clients,
        TemplateStore store,
        RouteApplier routes,
        CancellationToken ct)
    {
        var result = await store.ChangeAsync(id, template, clients, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await routes.FollowStandInsAsync(ct).ConfigureAwait(false);
        var members = await store.MembersAsync(ct).ConfigureAwait(false);

        return Results.Ok(TemplateAnswers.Template(result.Record!, Of(members, id)));
    }

    private static IReadOnlyList<long> Of(IReadOnlyDictionary<long, IReadOnlyList<long>> members, long id) =>
        members.GetValueOrDefault(id) ?? [];

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
