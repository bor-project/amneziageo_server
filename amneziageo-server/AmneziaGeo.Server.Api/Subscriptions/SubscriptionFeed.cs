using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
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

    private readonly DnsHolders _holders;

    private readonly PanelStore _panels;
    private readonly WebOptions _options;
    private readonly ProxyCertificates? _certificates;
    private readonly SubscriptionState? _subscriptions;
    private readonly TransportProxyStore? _proxies;
    private readonly PanelSettings? _runningPanel;
    private readonly ServiceServer? _runtime;

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
        DnsHolders holders,
        PanelStore panels,
        WebOptions? options = null,
        ProxyCertificates? certificates = null,
        SubscriptionState? subscriptions = null,
        TransportProxyStore? proxies = null,
        PanelSettings? runningPanel = null,
        ServiceServer? runtime = null)
    {
        _clients = clients;
        _configs = configs;
        _templates = templates;
        _ledger = ledger;
        _dns = dns;
        _resolver = resolver;
        _holders = holders;
        _panels = panels;
        _options = options ?? new WebOptions();
        _certificates = certificates;
        _subscriptions = subscriptions;
        _proxies = proxies;
        _runningPanel = runningPanel;
        _runtime = runtime;
    }

    /// <summary>
    /// Returns what a subscription hands out, null when no client carries it, naming the configurations with the host
    /// the subscription is read at for an endpoint that names none.
    /// </summary>
    public async Task<ClientFeed?> ReadAsync(string id, string host, CancellationToken ct)
    {
        var members = await _clients.SubscribedAsync(id, ct).ConfigureAwait(false);
        if (members.Count == 0)
        {
            return null;
        }

        var endpoints = await _configs.ListAsync(ct).ConfigureAwait(false);
        var templates = (await _templates.ListAsync(ct).ConfigureAwait(false)).ToDictionary(one => one.Id);
        var held = await _dns.ReadAsync(ct).ConfigureAwait(false);
        var handing = _resolver.Handing ?? held with { IsEnabled = false };
        var offering = _resolver.Chosen ?? held;
        var keptOut = _resolver.KeptOut;
        var settings = await _panels.ReadAsync(ct).ConfigureAwait(false);
        var naming = settings.NameTemplate;
        var configuredProxies = _proxies is null ? Array.Empty<TransportProxyEntity>() : await _proxies.ListAsync(ct).ConfigureAwait(false);

        var listenerFiles = new Dictionary<int, ServiceCertificateFiles?>();
        if (_runtime is not null)
        {
            var used = members.Where(member => member.IsEnabled && member.PrivateKey.Length > 0).Select(member => member.ConfigId).ToHashSet();
            foreach (var port in endpoints.Where(endpoint => endpoint.IsEnabled && used.Contains(endpoint.Id)).Select(ConfigServices.Port).Distinct())
            {
                listenerFiles.Add(port, await _runtime.CertificateFilesAsync(port, ct).ConfigureAwait(false));
            }
        }

        var identities = new Dictionary<long, ServiceTlsIdentity?>();
        var feed = ClientFeed.Of(
            endpoints,
            members,
            templates,
            _ledger.Used,
            endpoint => DnsHandout.For(endpoint, handing, keptOut),
            (endpoint, member) => ClientText.Title(endpoint, member, naming, host),
            endpoint => DnsHandout.For(endpoint, offering, keptOut),
            held.Files,
            Trust);
        await KeepAsync(
            endpoints,
            members,
            templates,
            endpoint => DnsHandout.For(endpoint, handing, keptOut),
            endpoint => DnsHandout.For(endpoint, offering, keptOut),
            held.Files,
            ct).ConfigureAwait(false);

        return feed;

        ServiceTlsIdentity? Trust(ServerConfig endpoint)
        {
            if (identities.TryGetValue(endpoint.Id, out var identity))
            {
                return identity;
            }

            var files = listenerFiles.GetValueOrDefault(ConfigServices.Port(endpoint));
            var selected = _runtime is not null && files is null ? null
                : ServiceTrust.Of(endpoint, _options, _runtime is null ? _runningPanel ?? settings : settings, publicName: host,
                    subscription: _subscriptions?.Current, proxies: _certificates, configured: configuredProxies, selectedFiles: files);
            identities.Add(endpoint.Id, selected);
            return selected;
        }
    }

    // Keeps which of the clients the subscription hands out were handed a file that names the resolver.
    private Task KeepAsync(
        IReadOnlyList<ServerConfig> endpoints,
        IReadOnlyList<TunnelClient> members,
        IReadOnlyDictionary<long, ClientTemplate> templates,
        Func<ServerConfig, IReadOnlyList<string>> resolver,
        Func<ServerConfig, IReadOnlyList<string>> panel,
        DnsFiles files,
        CancellationToken ct)
    {
        var named = new List<long>();
        var unnamed = new List<long>();
        foreach (var endpoint in endpoints.Where(one => one.IsEnabled))
        {
            var names = resolver(endpoint);
            var picked = panel(endpoint);
            foreach (var member in members.Where(one => one.ConfigId == endpoint.Id && one.IsEnabled && one.PrivateKey.Length > 0))
            {
                var template = member.TemplateId is { } chosen ? templates.GetValueOrDefault(chosen) : null;
                (ClientText.Dns(endpoint, member, template, names, picked, files).IsPanel ? named : unnamed).Add(member.Id);
            }
        }

        return _holders.HandedAsync(_clients, named, unnamed, ct);
    }
}
