using System.Text.Json;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Configs;

public static partial class ConfigEndpoints
{
    private static async Task<IResult> PreviewAsync(long id, ConfigRequest request, ConfigStore store, ConfigChangeReader changes, CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Refuse(404, "unknown-config", "the interface no longer exists");
        }
        var draft = ConfigAnswers.Draft(request, held);
        if (ConfigRules.Check(draft) is { } invalid)
        {
            return Refuse(400, invalid.Code, invalid.Message);
        }
        return Results.Ok(await changes.PreviewAsync(held, draft, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> PendingAsync(long id, ConfigStore store, ConfigDraftStore drafts, ConfigChangeReader changes, CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        var pending = await drafts.FindAsync(id, ct).ConfigureAwait(false);
        if (held is null || pending is null)
        {
            return Results.NoContent();
        }
        var request = JsonSerializer.Deserialize<ConfigRequest>(pending.Payload) ?? throw new InvalidDataException("the pending interface change is empty");
        var impact = await changes.PreviewAsync(held, ConfigAnswers.Draft(request, held), ct).ConfigureAwait(false);
        return Results.Ok(new PendingConfigBody(id, held.Name, pending.CreatedUtc,
            ConfigDraftStore.Revision(held) != pending.BaseRevision || request.ExpectedClientsRevision != impact.ClientsRevision, impact));
    }

    private static async Task<IResult> CancelPendingAsync(long id, ConfigDraftStore drafts, EndpointKeeper keeper, CancellationToken ct)
    {
        using var hold = await keeper.HoldAsync(ct).ConfigureAwait(false);
        await drafts.RemoveAsync(id, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ApplyPendingAsync(long id, HttpContext http, ConfigStore store, ConfigDraftStore drafts, ConfigChangeReader changes,
        ClientStore clients, RouteStore rules, EndpointHost host, EndpointKeeper keeper, RouteApplier routes,
        DnsHost resolver, ServiceServer services, FirewallApplier firewall, CancellationToken ct)
    {
        var pending = await drafts.FindAsync(id, ct).ConfigureAwait(false);
        if (pending is null)
        {
            return Refuse(404, "no-pending-config", "the interface has no pending change");
        }
        var request = JsonSerializer.Deserialize<ConfigRequest>(pending.Payload) ?? throw new InvalidDataException("the pending interface change is empty");
        return await ChangeAsync(id, request with { Defer = false, ExpectedRevision = pending.BaseRevision, ExpectedDraftRevision = ConfigDraftStore.PayloadRevision(pending.Payload) }, http,
            store, drafts, changes, clients, rules, host, keeper, routes, resolver, services, firewall, ct).ConfigureAwait(false);
    }
}
