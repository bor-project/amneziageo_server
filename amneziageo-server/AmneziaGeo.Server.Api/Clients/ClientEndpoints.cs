using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Proxy;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Traffic;

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
        writing.MapPost("/import", ImportAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapPost("/{id:long}/switch", SwitchAsync);
        writing.MapPost("/{id:long}/devices", AddDeviceAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        long? config,
        HttpContext context,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        ClientGuard guard,
        TrafficLedger ledger,
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
                answers.Add(ClientAnswers.Client(
                    mine[index],
                    endpoint.Name,
                    Seen(states[index], endpoint, mine[index], guard),
                    secrets,
                    guard.Cut(endpoint.Name, mine[index].PublicKey),
                    ledger.Of(mine[index])));
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
        ClientGuard guard,
        TrafficLedger ledger,
        CancellationToken ct)
    {
        var client = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-client", $"there is no client under the number {id}");
        }

        var endpoint = await configs.FindAsync(client.ConfigId, ct).ConfigureAwait(false);

        return Results.Ok(Answer(client, endpoint, host, guard, ledger, Secrets(context)));
    }

    private static async Task<IResult> DraftAsync(
        long? config,
        string? name,
        ConfigStore configs,
        ClientStore store,
        CancellationToken ct)
    {
        var endpoint = config is null
            ? ClientDefaults.Endpoint(
                await configs.ListAsync(ct).ConfigureAwait(false),
                await store.ListAsync(ct).ConfigureAwait(false))
            : await configs.FindAsync(config.Value, ct).ConfigureAwait(false);
        if (config is not null && endpoint is null)
        {
            return Missing(config.Value);
        }

        var wanted = string.IsNullOrWhiteSpace(name) ? "client" : name.Trim();
        var free = await store.FreeNameAsync(wanted, ct).ConfigureAwait(false);
        var address = await FreeAddressAsync(endpoint, store, ct).ConfigureAwait(false);
        var fresh = ClientDefaults.Fresh(endpoint?.Id ?? 0, free) with { Address = address };

        return Results.Ok(ClientAnswers.Client(
            fresh,
            endpoint?.Name ?? string.Empty,
            ClientState.Missing(fresh),
            true,
            [],
            ClientTraffic.None));
    }

    private static async Task<TunnelClient?> FilledAsync(
        TunnelClient draft,
        ConfigStore configs,
        ClientStore store,
        CancellationToken ct)
    {
        if (draft.Address.Count > 0 && draft.PublicKey.Length > 0)
        {
            return draft;
        }

        var endpoint = await configs.FindAsync(draft.ConfigId, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return null;
        }

        var keyed = ClientDefaults.Keyed(draft);

        return keyed.Address.Count > 0
            ? keyed
            : keyed with { Address = await FreeAddressAsync(endpoint, store, ct).ConfigureAwait(false) };
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
        HttpContext context,
        ConfigStore configs,
        ClientStore store,
        TemplateStore templates,
        DnsStore dns,
        DnsState resolver,
        SubscriptionState subscriptions,
        PanelSettings panel,
        WebOptions options,
        WebSocketFronts fronts,
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

        var names = DnsHandout.For(endpoint, resolver.Settings ?? await dns.ReadAsync(ct).ConfigureAwait(false));
        var front = (await fronts.ReadAsync(ct).ConfigureAwait(false))(endpoint);

        return Results.Ok(new ClientConfigResponse(
            ClientText.FileName(endpoint, client),
            ClientText.Text(endpoint, client, template, front, names),
            ClientLink.Link(endpoint, client, template, front, names),
            SubscriptionAnswer.Address(
                subscriptions.Current,
                panel,
                Listening.Chain(options, panel).Length > 0,
                context.Request.Host.Host,
                client.PrivateKey.Length > 0 ? client.SubscriptionId : string.Empty)));
    }

    private static async Task<IResult> AddAsync(
        ClientRequest request,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        EndpointHost endpoints,
        ClientGuard guard,
        TrafficLedger ledger,
        RouteApplier routes,
        CancellationToken ct)
    {
        var draft = ClientAnswers.Draft(request, null);
        if (await FilledAsync(draft, configs, store, ct).ConfigureAwait(false) is not { } settled)
        {
            return Missing(draft.ConfigId);
        }

        var result = await store.AddAsync(settled, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var endpoint = await SettleAsync(result.Record!.ConfigId, [], configs, store, host, endpoints, ct)
            .ConfigureAwait(false);

        await routes.FollowClientsAsync(ct).ConfigureAwait(false);

        return Results.Created(
            $"/api/clients/{result.Record.Id}",
            Answer(result.Record, endpoint, host, guard, ledger, true));
    }

    private static async Task<IResult> AddDeviceAsync(
        long id,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        EndpointHost endpoints,
        ClientGuard guard,
        TrafficLedger ledger,
        RouteApplier routes,
        CancellationToken ct)
    {
        var result = await store.AddDeviceAsync(id, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var endpoint = await SettleAsync(result.Record!.ConfigId, [], configs, store, host, endpoints, ct)
            .ConfigureAwait(false);

        await routes.FollowClientsAsync(ct).ConfigureAwait(false);

        return Results.Created(
            $"/api/clients/{result.Record.Id}",
            Answer(result.Record, endpoint, host, guard, ledger, true));
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        ClientRequest request,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        EndpointHost endpoints,
        ClientGuard guard,
        TrafficLedger ledger,
        RouteApplier routes,
        CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-client", $"there is no client under the number {id}");
        }

        var result = await store.ChangeAsync(id, ClientAnswers.Draft(request, held), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var gone = string.Equals(held.PublicKey, result.Record!.PublicKey, StringComparison.Ordinal)
            ? Array.Empty<string>()
            : [held.PublicKey];
        var endpoint = await SettleAsync(result.Record.ConfigId, gone, configs, store, host, endpoints, ct)
            .ConfigureAwait(false);

        await routes.FollowClientsAsync(ct).ConfigureAwait(false);

        return Results.Ok(Answer(result.Record, endpoint, host, guard, ledger, true));
    }

    private static async Task<IResult> SwitchAsync(
        long id,
        ClientSwitchRequest request,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        EndpointHost endpoints,
        ClientGuard guard,
        TrafficLedger ledger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await store.SwitchAsync(id, request.On, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var endpoint = await SettleAsync(result.Record!.ConfigId, [], configs, store, host, endpoints, ct)
            .ConfigureAwait(false);

        return Results.Ok(Answer(result.Record, endpoint, host, guard, ledger, true));
    }

    private static async Task<IResult> RemoveAsync(
        long id,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        EndpointHost endpoints,
        RouteApplier routes,
        CancellationToken ct)
    {
        var devices = await store.DevicesAsync(id, ct).ConfigureAwait(false);
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await SettleAsync(
                result.Record!.ConfigId,
                [result.Record.PublicKey, .. devices.Select(device => device.PublicKey)],
                configs,
                store,
                host,
                endpoints,
                ct)
            .ConfigureAwait(false);

        await routes.FollowClientsAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult> ImportAsync(
        ClientImportRequest request,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        EndpointHost endpoints,
        RouteApplier routes,
        CancellationToken ct)
    {
        var endpoint = await configs.FindAsync(request.ConfigId, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-config", $"there is no endpoint under the number {request.ConfigId}");
        }

        var read = ClientImport.Read(request.Text, endpoint.Name, endpoint.Id, request.Prefix);
        if (read.Fault is not null)
        {
            return Refuse(StatusCodes.Status400BadRequest, read.Fault.Code, read.Fault.Message);
        }

        var report = await store.ImportAllAsync(read.Clients, ct).ConfigureAwait(false);
        await SettleAsync(endpoint.Id, [], configs, store, host, endpoints, ct).ConfigureAwait(false);

        await routes.FollowClientsAsync(ct).ConfigureAwait(false);

        return Results.Ok(report);
    }

    private static async Task<IResult> ApplyAsync(
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        EndpointHost endpoints,
        CancellationToken ct)
    {
        var found = await configs.ListAsync(ct).ConfigureAwait(false);
        var answers = new List<ClientApplyBody>(found.Count);
        foreach (var endpoint in found)
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

        await FirewallAsync(configs, store, endpoints, ct).ConfigureAwait(false);

        return Results.Ok(answers);
    }

    private static async Task<ServerConfig?> SettleAsync(
        long configId,
        IReadOnlyList<string> gone,
        ConfigStore configs,
        ClientStore store,
        ClientHost host,
        EndpointHost endpoints,
        CancellationToken ct)
    {
        var endpoint = await configs.FindAsync(configId, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return null;
        }

        var clients = await store.ListAsync(configId, ct).ConfigureAwait(false);
        await host.SyncAsync(endpoint, clients, gone, ct).ConfigureAwait(false);
        await FirewallAsync(configs, store, endpoints, ct).ConfigureAwait(false);

        return endpoint;
    }

    private static async Task FirewallAsync(
        ConfigStore configs,
        ClientStore store,
        EndpointHost endpoints,
        CancellationToken ct)
    {
        await endpoints.FirewallAsync(
                await configs.ListAsync(ct).ConfigureAwait(false),
                await store.ListAsync(ct).ConfigureAwait(false),
                ct)
            .ConfigureAwait(false);
    }

    private static ClientResponse Answer(
        TunnelClient client,
        ServerConfig? endpoint,
        ClientHost host,
        ClientGuard guard,
        TrafficLedger ledger,
        bool secrets)
    {
        var state = endpoint is null
            ? ClientState.Missing(client)
            : Seen(host.States(endpoint, [client])[0], endpoint, client, guard);
        var cut = endpoint is null ? Array.Empty<string>() : guard.Cut(endpoint.Name, client.PublicKey);

        return ClientAnswers.Client(client, endpoint?.Name ?? string.Empty, state, secrets, cut, ledger.Of(client));
    }

    private static ClientState Seen(ClientState state, ServerConfig endpoint, TunnelClient client, ClientGuard guard) =>
        guard.Online(endpoint, client.PublicKey) is { } online ? state with { IsOnline = online } : state;

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
