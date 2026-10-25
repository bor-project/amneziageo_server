using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Updates;
using Microsoft.AspNetCore.Http.Features;

namespace AmneziaGeo.Server.Api.ClientUpdates;

/// <summary>
/// The routes the releases of the client are set up and filled through.
/// </summary>
public static class ClientUpdateEndpoints
{
    /// <summary>
    /// Maps the routes of the releases of the client.
    /// </summary>
    public static IEndpointRouteBuilder MapClientUpdates(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/client-updates").RequireScope(Scopes.ManageClientUpdates);
        group.MapGet("/", ReadAsync);
        group.MapPut("/", SaveAsync);
        group.MapPost("/check", CheckAsync);
        group.MapGet("/releases", (int page, int size, ClientUpdateServer server, CancellationToken ct) =>
            AnswerAsync(server, async hub => Results.Ok(await hub.ListAsync(page > 0 ? page : 1, size > 0 ? size : 20, ct).ConfigureAwait(false))));
        group.MapPost("/releases", (ReleaseDraft draft, ClientUpdateServer server, CancellationToken ct) =>
            AnswerAsync(server, async hub => Results.Ok(await hub.CreateAsync(draft, ct).ConfigureAwait(false))));
        group.MapGet("/releases/{tag}", (string tag, ClientUpdateServer server, CancellationToken ct) =>
            AnswerAsync(server, async hub => Results.Ok(await hub.ReadAsync(tag, ct).ConfigureAwait(false))));
        group.MapPatch("/releases/{tag}", (string tag, ReleaseChange change, ClientUpdateServer server, CancellationToken ct) =>
            AnswerAsync(server, async hub => Results.Ok(await hub.ChangeAsync(tag, change, ct).ConfigureAwait(false))));
        group.MapDelete("/releases/{tag}", (string tag, ClientUpdateServer server, CancellationToken ct) =>
            AnswerAsync(server, async hub =>
            {
                await hub.DeleteAsync(tag, ct).ConfigureAwait(false);

                return Results.NoContent();
            }));
        group.MapPut("/releases/{tag}/files/{name}", (string tag, string name, string? from, HttpContext http, ClientUpdateServer server, CancellationToken ct) =>
            AnswerAsync(server, async hub =>
            {
                Unbound(http);

                return Results.Ok(await hub.PutFileAsync(tag, name, http.Request.Body, from, ct).ConfigureAwait(false));
            }));
        group.MapDelete("/releases/{tag}/files/{name}", (string tag, string name, ClientUpdateServer server, CancellationToken ct) =>
            AnswerAsync(server, async hub => Results.Ok(await hub.DropFileAsync(tag, name, ct).ConfigureAwait(false))));
        group.MapPost("/releases/{tag}/archive", (string tag, HttpContext http, ClientUpdateServer server, CancellationToken ct) =>
            AnswerAsync(server, async hub =>
            {
                Unbound(http);

                return Results.Ok(await hub.ImportAsync(tag, http.Request.Body, ct).ConfigureAwait(false));
            }));

        return routes;
    }

    private static async Task<IResult> ReadAsync(
        ClientUpdateStore store,
        ClientUpdateState state,
        ClientUpdateServer server,
        PanelSettings panel,
        HttpContext http,
        CancellationToken ct)
    {
        var settings = await store.ReadAsync(ct).ConfigureAwait(false);

        return Results.Ok(ClientUpdateAnswers.Settings(settings, state, await StatusAsync(server, ct).ConfigureAwait(false), panel, http.Request.Host.Host));
    }

    private static async Task<IResult> SaveAsync(
        ClientUpdateRequest request,
        ClientUpdateStore store,
        ConfigStore configs,
        ClientUpdateServer server,
        ClientUpdateState state,
        SubscriptionState subscriptions,
        PanelSettings panel,
        HttpContext http,
        CancellationToken ct)
    {
        var held = await store.ReadAsync(ct).ConfigureAwait(false);
        var draft = ClientUpdateAnswers.Draft(request, held);
        if (draft.Own && draft.Path.Length == 0)
        {
            draft = draft with { Path = PanelDefaults.FreshPath() };
        }

        if (draft.Own && draft.Token.Length == 0)
        {
            draft = draft with { Token = UpdateToken.Make() };
        }

        if (ClientUpdateRules.Check(draft, panel) is { } fault)
        {
            return Refuse(fault.Code, fault.Message);
        }

        if (draft.IsEnabled && draft.Own)
        {
            var port = draft.PortOr(panel.Port);
            var subscription = subscriptions.Current;
            if (subscription.IsEnabled && subscription.Separate && subscription.Port == port
                && string.Equals(Head(subscription.Path), Head(draft.Path), StringComparison.OrdinalIgnoreCase))
            {
                return Refuse("updates-path-taken", $"the subscriptions answer under '/{Head(draft.Path)}/' on TCP port {port}");
            }

            if (await configs.SocketUnderAsync(port == panel.Port ? null : port, draft.Path, ct).ConfigureAwait(false) is { } socket)
            {
                return Refuse(
                    "updates-path-taken",
                    $"the websocket of {socket.Name} comes under '/{ConfigServices.WebSocketPath(socket)}' on TCP port {ConfigServices.Port(socket)}");
            }
        }

        var refused = await server.ApplyAsync(draft, ct).ConfigureAwait(false);
        if (refused.Length > 0)
        {
            return Refuse(refused, state.Detail.Length > 0 ? state.Detail : "the host did not serve the releases of the client");
        }

        var saved = await store.SaveAsync(draft, ct).ConfigureAwait(false);

        return Results.Ok(ClientUpdateAnswers.Settings(saved, state, await StatusAsync(server, ct).ConfigureAwait(false), panel, http.Request.Host.Host));
    }

    // Asks the service apart now and answers as a read does.
    private static async Task<IResult> CheckAsync(
        ClientUpdateStore store,
        ClientUpdateState state,
        ClientUpdateServer server,
        PanelSettings panel,
        HttpContext http,
        CancellationToken ct)
    {
        var settings = await store.ReadAsync(ct).ConfigureAwait(false);
        var status = settings.Own
            ? await StatusAsync(server, ct).ConfigureAwait(false)
            : await server.CheckAsync(ct).ConfigureAwait(false);

        return Results.Ok(ClientUpdateAnswers.Settings(settings, state, status, panel, http.Request.Host.Host));
    }

    // Returns what the store says about itself, null while the releases are off or it does not answer.
    private static async Task<StoreStatus?> StatusAsync(ClientUpdateServer server, CancellationToken ct)
    {
        if (server.Hub is not { } hub)
        {
            return null;
        }

        try
        {
            return await hub.StatusAsync(ct).ConfigureAwait(false);
        }
        catch (ReleaseRefused)
        {
            return null;
        }
    }

    private static async Task<IResult> AnswerAsync(ClientUpdateServer server, Func<IReleaseHub, Task<IResult>> answer)
    {
        if (server.Hub is not { } hub)
        {
            return Results.Json(new UpdateFailure("updates-off", "the releases of the client are not served"), statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            return await answer(hub).ConfigureAwait(false);
        }
        catch (ReleaseRefused refused)
        {
            return Results.Json(new UpdateFailure(refused.Code, refused.Message, refused.File), statusCode: refused.Status);
        }
    }

    // Lets a body of any size in: the store bounds it itself.
    private static void Unbound(HttpContext context)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = null;
        }
    }

    private static string Head(string path) => path.Trim('/').Split('/')[0];

    private static IResult Refuse(string code, string message) =>
        Results.Json(new Failure(code, message), statusCode: StatusCodes.Status400BadRequest);
}
