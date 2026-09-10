using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Geo.Files;
using AmneziaGeo.Server.Routing.Dns;
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

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public TemplateRefresher(GeoStore geo, IGeoFileStore files, DnsStore dns, DnsState resolver, TimeProvider time)
    {
        _geo = geo;
        _files = files;
        _dns = dns;
        _resolver = resolver;
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

        var sources = await _geo.ListAsync(ct).ConfigureAwait(false);
        var settings = _resolver.Settings ?? await _dns.ReadAsync(ct).ConfigureAwait(false);
        var resolver = new TemplateResolver(new DnsUpstream(settings.Upstreams, Wait));
        var found = await resolver.ResolveAsync(template.Entries, GeoIndex.Load(sources, _files), ct).ConfigureAwait(false);

        return template with { AllowedIps = found.AllowedIps, Missed = found.Missed, RefreshedUtc = _time.GetUtcNow() };
    }
}
