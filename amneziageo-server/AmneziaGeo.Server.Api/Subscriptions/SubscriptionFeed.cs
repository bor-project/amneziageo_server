using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Hello;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
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

    private readonly DnsStore _dns;

    private readonly DnsState _resolver;

    private readonly HelloOptions _hello;

    /// <summary>
    /// ctor
    /// </summary>
    public SubscriptionFeed(
        ClientStore clients,
        ConfigStore configs,
        TemplateStore templates,
        TrafficLedger ledger,
        DnsStore dns,
        DnsState resolver,
        HelloOptions hello)
    {
        _clients = clients;
        _configs = configs;
        _templates = templates;
        _ledger = ledger;
        _dns = dns;
        _resolver = resolver;
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
        var settings = _resolver.Settings ?? await _dns.ReadAsync(ct).ConfigureAwait(false);

        return ClientFeed.Of(
            endpoints,
            members,
            templates.ToDictionary(one => one.Id),
            _ledger.Group,
            _hello.Port,
            endpoint => DnsHandout.For(endpoint, settings));
    }
}
