using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Geo.Files;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// Fills the ranges of a client template from its entries.
/// </summary>
public sealed class TemplateRefresher
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(2);

    private readonly GeoStore _geo;

    private readonly IGeoFileStore _files;

    private readonly DnsStore _dns;

    private readonly DnsState _resolver;

    private readonly OutboundStore _outbounds;

    private readonly BalanceStore _balancers;

    private readonly BalanceLive _live;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public TemplateRefresher(
        GeoStore geo,
        IGeoFileStore files,
        DnsStore dns,
        DnsState resolver,
        OutboundStore outbounds,
        BalanceStore balancers,
        BalanceLive live,
        TimeProvider time)
    {
        _geo = geo;
        _files = files;
        _dns = dns;
        _resolver = resolver;
        _outbounds = outbounds;
        _balancers = balancers;
        _live = live;
        _time = time;
    }

    /// <summary>
    /// Returns the template with the ranges its entries stand for now.
    /// </summary>
    public async Task<ClientTemplate> ResolveAsync(ClientTemplate template, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (template.Entries.Count == 0)
        {
            return template with { AllowedIps = [], Missed = [], RefreshedUtc = null };
        }

        var found = await FindAsync(template.Entries, ct).ConfigureAwait(false);

        return template with { AllowedIps = found.AllowedIps, Missed = found.Missed, RefreshedUtc = _time.GetUtcNow() };
    }

    /// <summary>
    /// Returns what the entries stand for now.
    /// </summary>
    public async Task<TemplateResolution> FindAsync(IReadOnlyList<string> entries, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return new TemplateResolution([], [], []);
        }

        var sources = await _geo.ListAsync(ct).ConfigureAwait(false);
        var settings = _resolver.Settings ?? await _dns.ReadAsync(ct).ConfigureAwait(false);
        var outbounds = await _outbounds.ListAsync(ct).ConfigureAwait(false);
        var balancers = await _balancers.ListAsync(ct).ConfigureAwait(false);
        var way = DnsExit.Way(settings, new RouteWays(outbounds, balancers, _live.Alive));
        var resolver = new TemplateResolver(new DnsUpstream(settings.Upstreams, Wait, () => way.Mark));

        return await resolver.ResolveAsync(entries, GeoIndex.Load(sources, _files), ct).ConfigureAwait(false);
    }
}
