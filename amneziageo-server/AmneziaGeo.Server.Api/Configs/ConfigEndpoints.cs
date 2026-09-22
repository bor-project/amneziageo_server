using System.Security.Cryptography;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// The routes the server endpoints are managed through.
/// </summary>
public static class ConfigEndpoints
{
    /// <summary>
    /// Maps the endpoint management routes.
    /// </summary>
    public static IEndpointRouteBuilder MapConfigs(this IEndpointRouteBuilder routes)
    {
        var reading = routes.MapGroup("/api/configs").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/{id:long}", FindAsync);

        var writing = routes.MapGroup("/api/configs").RequireScope(Scopes.ManageInterfaces);
        writing.MapGet("/draft", DraftAsync);
        writing.MapPost("/keys", Keys);
        writing.MapPost("/preshared", Preshared);
        writing.MapPost("/import", Import);
        writing.MapPost("/", AddAsync);
        writing.MapPost("/apply", ApplyAllAsync);
        writing.MapPost("/{id:long}/apply", ApplyAsync);
        writing.MapPost("/{id:long}/switch", SwitchAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(HttpContext context, ConfigStore store, CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var secrets = Secrets(context);

        return Results.Ok(found.Select(config => ConfigAnswers.Config(config, secrets)).ToArray());
    }

    private static async Task<IResult> FindAsync(long id, HttpContext context, ConfigStore store, CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);

        return found is null
            ? Refuse(StatusCodes.Status404NotFound, "unknown-config", $"there is no endpoint under the number {id}")
            : Results.Ok(ConfigAnswers.Config(found, Secrets(context)));
    }

    private static async Task<IResult> DraftAsync(string? name, ConfigStore store, CancellationToken ct)
    {
        var wanted = string.IsNullOrWhiteSpace(name) ? "awg0" : name.Trim();
        var fresh = ConfigDefaults.Fresh(wanted);
        var port = await store.FreePortAsync(fresh.ListenPort, ct).ConfigureAwait(false);

        return Results.Ok(ConfigAnswers.Config(fresh with { ListenPort = port }, true));
    }

    private static IResult Keys()
    {
        var pair = Curve25519.Create();

        return Results.Ok(new KeyPairResponse(pair.PrivateKey, pair.PublicKey));
    }

    private static IResult Preshared() =>
        Results.Ok(new KeyResponse(Convert.ToBase64String(RandomNumberGenerator.GetBytes(Curve25519.KeySize))));

    private static IResult Import(ConfigImportRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var result = ConfigImport.Read(request.Text, name.Length > 0 ? name : "awg0");

        return result.Config is null
            ? Refuse(StatusCodes.Status400BadRequest, result.Fault!.Code, result.Fault.Message)
            : Results.Ok(ConfigAnswers.Config(result.Config, true));
    }

    private static async Task<IResult> AddAsync(
        ConfigRequest request,
        ConfigStore store,
        ClientStore clients,
        EndpointHost host,
        RouteApplier routes,
        DnsHost resolver,
        ServiceServer services,
        FirewallApplier firewall,
        CancellationToken ct)
    {
        var result = await store.AddAsync(ConfigAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var raised = await RaiseAsync(store, clients, host, result.Record!, ct).ConfigureAwait(false);
        await routes.SettleAsync(ct).ConfigureAwait(false);
        await resolver.RebindAsync(ct).ConfigureAwait(false);
        await services.SettleAsync(ct).ConfigureAwait(false);
        await firewall.SettleAsync(ct).ConfigureAwait(false);

        return raised.IsDone
            ? Results.Created($"/api/configs/{result.Record!.Id}", ConfigAnswers.Config(result.Record, true))
            : Downed(raised, result.Record!.Id);
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        ConfigRequest request,
        ConfigStore store,
        ClientStore clients,
        EndpointHost host,
        RouteApplier routes,
        DnsHost resolver,
        ServiceServer services,
        FirewallApplier firewall,
        CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        var result = await store.ChangeAsync(id, ConfigAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        if (held is not null && !string.Equals(held.Name, result.Record!.Name, StringComparison.Ordinal))
        {
            await host.WithdrawAsync(held.Name, ct).ConfigureAwait(false);
        }

        var raised = await RaiseAsync(store, clients, host, result.Record!, ct).ConfigureAwait(false);
        await routes.SettleAsync(ct).ConfigureAwait(false);
        await resolver.RebindAsync(ct).ConfigureAwait(false);
        await services.SettleAsync(ct).ConfigureAwait(false);
        await firewall.SettleAsync(ct).ConfigureAwait(false);

        return raised.IsDone ? Results.Ok(ConfigAnswers.Config(result.Record!, true)) : Downed(raised, id);
    }

    private static async Task<IResult> SwitchAsync(
        long id,
        ConfigSwitchRequest request,
        ConfigStore store,
        ClientStore clients,
        EndpointHost host,
        RouteApplier routes,
        DnsHost resolver,
        ServiceServer services,
        FirewallApplier firewall,
        CancellationToken ct)
    {
        if (request.On is not { } on)
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "a switch needs the on field");
        }

        var result = await store.SwitchAsync(id, on, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var raised = await RaiseAsync(store, clients, host, result.Record!, ct).ConfigureAwait(false);
        await routes.SettleAsync(ct).ConfigureAwait(false);
        await resolver.RebindAsync(ct).ConfigureAwait(false);
        await services.SettleAsync(ct).ConfigureAwait(false);
        await firewall.SettleAsync(ct).ConfigureAwait(false);

        return raised.IsDone ? Results.Ok(ConfigAnswers.Config(result.Record!, true)) : Downed(raised, id);
    }

    private static async Task<IResult> RemoveAsync(
        long id,
        ConfigStore store,
        ClientStore clients,
        EndpointHost host,
        RouteApplier routes,
        DnsHost resolver,
        ServiceServer services,
        FirewallApplier firewall,
        CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await host.WithdrawAsync(result.Record!.Name, ct).ConfigureAwait(false);
        await FirewallAsync(store, clients, host, ct).ConfigureAwait(false);
        await routes.SettleAsync(ct).ConfigureAwait(false);
        await resolver.RebindAsync(ct).ConfigureAwait(false);
        await services.SettleAsync(ct).ConfigureAwait(false);
        await firewall.SettleAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult> ApplyAsync(
        long id,
        ConfigStore store,
        ClientStore clients,
        EndpointHost host,
        ServiceServer services,
        FirewallApplier firewall,
        CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-config", $"there is no endpoint under the number {id}");
        }

        var sync = await host.ApplyAsync(found, ct).ConfigureAwait(false);
        await FirewallAsync(store, clients, host, ct).ConfigureAwait(false);
        await services.SettleAsync(ct).ConfigureAwait(false);
        await firewall.SettleAsync(ct).ConfigureAwait(false);

        return Results.Ok(new ConfigSyncResponse(sync.Name, sync.IsDone, sync.Message));
    }

    private static async Task<IResult> ApplyAllAsync(
        ConfigStore store,
        ClientStore clients,
        EndpointHost host,
        ServiceServer services,
        FirewallApplier firewall,
        CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var done = await host.SyncAsync(found, await clients.ListAsync(ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
        await services.SettleAsync(ct).ConfigureAwait(false);
        await firewall.SettleAsync(ct).ConfigureAwait(false);

        return Results.Ok(done.Select(sync => new ConfigSyncResponse(sync.Name, sync.IsDone, sync.Message)).ToArray());
    }

    private static async Task<EndpointSync> RaiseAsync(
        ConfigStore store,
        ClientStore clients,
        EndpointHost host,
        ServerConfig config,
        CancellationToken ct)
    {
        var sync = await host.ApplyAsync(config, ct).ConfigureAwait(false);
        if (!sync.IsDone && config.IsEnabled)
        {
            await host.WithdrawAsync(config.Name, ct).ConfigureAwait(false);
            await store.SwitchAsync(config.Id, false, ct).ConfigureAwait(false);
        }

        await FirewallAsync(store, clients, host, ct).ConfigureAwait(false);

        return sync;
    }

    private static async Task FirewallAsync(
        ConfigStore store,
        ClientStore clients,
        EndpointHost host,
        CancellationToken ct)
    {
        await host.FirewallAsync(
                await store.ListAsync(ct).ConfigureAwait(false),
                await clients.ListAsync(ct).ConfigureAwait(false),
                ct)
            .ConfigureAwait(false);
    }

    private static bool Secrets(HttpContext context) =>
        context.Caller()?.Holds(Scopes.ManageInterfaces) == true;

    private static IResult Downed(EndpointSync sync, long id) => Results.Json(
        new ConfigRaiseFailure(sync.Fault, sync.Message, id),
        statusCode: StatusCodes.Status409Conflict);

    private static IResult Explain(ConfigResult result) => Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(ConfigOutcome outcome) => outcome switch
    {
        ConfigOutcome.Unknown => StatusCodes.Status404NotFound,
        ConfigOutcome.NameTaken or ConfigOutcome.PortTaken => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
