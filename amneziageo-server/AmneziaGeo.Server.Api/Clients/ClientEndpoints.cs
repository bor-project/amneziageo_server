using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// The routes the clients of the endpoints are managed through.
/// </summary>
public static class ClientEndpoints
{
    /// <summary>
    /// Maps the client management routes.
    /// </summary>
    public static IEndpointRouteBuilder MapClients(this IEndpointRouteBuilder routes)
    {
        var reading = routes.MapGroup("/api/clients").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/{id:long}", FindAsync);

        var writing = routes.MapGroup("/api/clients").RequireScope(Scopes.ManageClients);
        writing.MapGet("/draft", DraftAsync);
        writing.MapGet("/{id:long}/config", TextAsync);
        writing.MapPost("/", AddAsync);
        writing.MapPost("/apply", ApplyAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapPost("/{id:long}/switch", SwitchAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        long? config,
        HttpContext context,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        CancellationToken ct)
    {
        var endpoints = await configs.ListAsync(ct).ConfigureAwait(false);
        var clients = config is null
            ? await store.ListAsync(ct).ConfigureAwait(false)
            : await store.ListAsync(config.Value, ct).ConfigureAwait(false);
        var secrets = Secrets(context);
        var answers = new List<ClientResponse>(clients.Count);
        foreach (var endpoint in endpoints)
        {
            var mine = clients.Where(client => client.ConfigId == endpoint.Id).ToArray();
            if (mine.Length == 0)
            {
                continue;
            }

            var states = host.States(endpoint, mine);
            for (var index = 0; index < mine.Length; index++)
            {
                answers.Add(ClientAnswers.Client(mine[index], endpoint.Name, states[index], secrets));
            }
        }

        return Results.Ok(answers);
    }

    private static async Task<IResult> FindAsync(
        long id,
        HttpContext context,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        CancellationToken ct)
    {
        var client = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-client", $"there is no client under the number {id}");
        }

        var endpoint = await configs.FindAsync(client.ConfigId, ct).ConfigureAwait(false);

        return Results.Ok(Answer(client, endpoint, host, Secrets(context)));
    }

    private static async Task<IResult> DraftAsync(
        long? config,
        string? name,
        ConfigStore configs,
        ClientStore store,
        CancellationToken ct)
    {
        var endpoint = config is null ? null : await configs.FindAsync(config.Value, ct).ConfigureAwait(false);
        if (config is not null && endpoint is null)
        {
            return Missing(config.Value);
        }

        var wanted = string.IsNullOrWhiteSpace(name) ? "client" : name.Trim();
        var free = await store.FreeNameAsync(wanted, ct).ConfigureAwait(false);
        var address = await FreeAddressAsync(endpoint, store, ct).ConfigureAwait(false);
        var fresh = ClientDefaults.Fresh(config ?? 0, free) with { Address = address };

        return Results.Ok(ClientAnswers.Client(fresh, endpoint?.Name ?? string.Empty, ClientState.Missing(fresh), true));
    }

    private static async Task<IReadOnlyList<string>> FreeAddressAsync(
        ServerConfig? endpoint,
        ClientStore store,
        CancellationToken ct)
    {
        if (endpoint is null)
        {
            return [];
        }

        var taken = await store.AddressesAsync(endpoint.Id, ct).ConfigureAwait(false);

        return ClientPool.Free(endpoint.Address, [.. taken, .. endpoint.Address]);
    }

    private static async Task<IResult> TextAsync(
        long id,
        ConfigStore configs,
        ClientStore store,
        TemplateStore templates,
        CancellationToken ct)
    {
        var client = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-client", $"there is no client under the number {id}");
        }

        var endpoint = await configs.FindAsync(client.ConfigId, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return Missing(client.ConfigId);
        }

        var template = client.TemplateId is { } chosen
            ? await templates.FindAsync(chosen, ct).ConfigureAwait(false)
            : null;

        return Results.Ok(new ClientConfigResponse(
            ClientText.FileName(endpoint, client),
            ClientText.Text(endpoint, client, template),
            ClientLink.Link(endpoint, client, template)));
    }

    private static async Task<IResult> AddAsync(
        ClientRequest request,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        CancellationToken ct)
    {
        var result = await store.AddAsync(ClientAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var endpoint = await SettleAsync(result.Record!.ConfigId, [], configs, store, host, ct).ConfigureAwait(false);

        return Results.Created(
            $"/api/clients/{result.Record.Id}",
            Answer(result.Record, endpoint, host, true));
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        ClientRequest request,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-client", $"there is no client under the number {id}");
        }

        var result = await store.ChangeAsync(id, ClientAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var gone = string.Equals(held.PublicKey, result.Record!.PublicKey, StringComparison.Ordinal)
            ? Array.Empty<string>()
            : [held.PublicKey];
        var endpoint = await SettleAsync(result.Record.ConfigId, gone, configs, store, host, ct).ConfigureAwait(false);

        return Results.Ok(Answer(result.Record, endpoint, host, true));
    }

    private static async Task<IResult> SwitchAsync(
        long id,
        ClientSwitchRequest request,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await store.SwitchAsync(id, request.On, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var endpoint = await SettleAsync(result.Record!.ConfigId, [], configs, store, host, ct).ConfigureAwait(false);

        return Results.Ok(Answer(result.Record, endpoint, host, true));
    }

    private static async Task<IResult> RemoveAsync(
        long id,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await SettleAsync(result.Record!.ConfigId, [result.Record.PublicKey], configs, store, host, ct)
            .ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult> ApplyAsync(
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        CancellationToken ct)
    {
        var endpoints = await configs.ListAsync(ct).ConfigureAwait(false);
        var answers = new List<ClientApplyBody>(endpoints.Count);
        foreach (var endpoint in endpoints)
        {
            var clients = await store.ListAsync(endpoint.Id, ct).ConfigureAwait(false);
            var sync = await host.SyncAsync(endpoint, clients, [], ct).ConfigureAwait(false);
            answers.Add(new ClientApplyBody(
                endpoint.Name,
                sync.IsDone,
                clients.Count(client => client.IsEnabled),
                host.Stale(endpoint, clients),
                sync.Message));
        }

        return Results.Ok(answers);
    }

    private static async Task<ServerConfig?> SettleAsync(
        long configId,
        IReadOnlyList<string> gone,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        CancellationToken ct)
    {
        var endpoint = await configs.FindAsync(configId, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return null;
        }

        var clients = await store.ListAsync(configId, ct).ConfigureAwait(false);
        await host.SyncAsync(endpoint, clients, gone, ct).ConfigureAwait(false);

        return endpoint;
    }

    private static ClientResponse Answer(TunnelClient client, ServerConfig? endpoint, ClientHost host, bool secrets)
    {
        var state = endpoint is null ? ClientState.Missing(client) : host.States(endpoint, [client])[0];

        return ClientAnswers.Client(client, endpoint?.Name ?? string.Empty, state, secrets);
    }

    private static bool Secrets(HttpContext context) => context.Caller()?.Holds(Scopes.ManageClients) == true;

    private static IResult Missing(long id) =>
        Refuse(StatusCodes.Status404NotFound, "unknown-config", $"there is no endpoint under the number {id}");

    private static IResult Explain(ClientResult result) => Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(ClientOutcome outcome) => outcome switch
    {
        ClientOutcome.Unknown or ClientOutcome.UnknownConfig => StatusCodes.Status404NotFound,
        ClientOutcome.NameTaken or ClientOutcome.KeyTaken or ClientOutcome.AddressTaken => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
