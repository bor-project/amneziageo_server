using AmneziaGeo.Server.Api.Hello;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Traffic;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// Gathers what a subscription hands out.
/// </summary>
public sealed class SubscriptionFeed
{
    private readonly ClientStore _clients;

    private readonly ConfigStore _configs;

    private readonly TemplateStore _templates;

    private readonly TrafficLedger _ledger;

    private readonly HelloOptions _hello;

    /// <summary>
    /// ctor
    /// </summary>
    public SubscriptionFeed(ClientStore clients, ConfigStore configs, TemplateStore templates, TrafficLedger ledger, HelloOptions hello)
    {
        _clients = clients;
        _configs = configs;
        _templates = templates;
        _ledger = ledger;
        _hello = hello;
    }

    /// <summary>
    /// Returns what a subscription hands out, null when no client carries it.
    /// </summary>
    public async Task<ClientFeed?> ReadAsync(string id, CancellationToken ct)
    {
        var members = await _clients.SubscribedAsync(id, ct).ConfigureAwait(false);
        if (members.Count == 0)
        {
            return null;
        }

        var endpoints = await _configs.ListAsync(ct).ConfigureAwait(false);
        var templates = await _templates.ListAsync(ct).ConfigureAwait(false);

        return ClientFeed.Of(endpoints, members, templates.ToDictionary(one => one.Id), _ledger.Group, _hello.Port);
    }
}
