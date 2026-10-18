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
/// What a pass over the entries of a template left.
/// </summary>
/// <param name="Template">The template with the ranges worked out, or as it was when the name servers kept silent.</param>
/// <param name="Silence">Why the name servers left the names unanswered, or null when they answered.</param>
public sealed record TemplateRenewal(ClientTemplate Template, string? Silence);

/// <summary>
/// Fills the ranges of a client template from its entries.
/// </summary>
public sealed class TemplateRefresher
{
    private const string NoAnswer = "no name server answered";

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

        var (found, _) = await AskAsync(template.Entries, template.StandIns, ct).ConfigureAwait(false);

        return Taken(template, found);
    }

    /// <summary>
    /// Returns the template with the ranges its entries stand for now, or as it was when the name servers kept silent.
    /// </summary>
    public async Task<TemplateRenewal> RenewAsync(ClientTemplate template, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);

        var (found, silence) = await AskAsync(template.Entries, template.StandIns, ct).ConfigureAwait(false);

        return silence is null ? new TemplateRenewal(Taken(template, found), null) : new TemplateRenewal(template, silence);
    }

    /// <summary>
    /// Returns what the entries stand for now, the names left to the stand-in addresses when they take them.
    /// </summary>
    public async Task<TemplateResolution> FindAsync(IReadOnlyList<string> entries, bool standIns, CancellationToken ct) =>
        (await AskAsync(entries, standIns, ct).ConfigureAwait(false)).Found;

    // Works the entries out and tells why the name servers left the names unanswered.
    private async Task<(TemplateResolution Found, string? Silence)> AskAsync(
        IReadOnlyList<string> entries,
        bool standIns,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return (new TemplateResolution([], [], []), null);
        }

        var sources = await _geo.ListAsync(ct).ConfigureAwait(false);
        var settings = _resolver.Settings ?? await _dns.ReadAsync(ct).ConfigureAwait(false);
        var outbounds = await _outbounds.ListAsync(ct).ConfigureAwait(false);
        var balancers = await _balancers.ListAsync(ct).ConfigureAwait(false);
        var way = DnsExit.Way(settings, new RouteWays(outbounds, balancers, _live.Alive));
        using var upstream = new DnsUpstream(settings.Upstreams, Wait, () => way.Mark);
        var tally = new Tally(upstream);
        var resolver = new TemplateResolver(tally);
        var found = await resolver.ResolveAsync(entries, GeoIndex.Load(sources, _files), standIns, ct).ConfigureAwait(false);

        return (found, tally.Silence(upstream.Fault, way.Fault?.Message));
    }

    private ClientTemplate Taken(ClientTemplate template, TemplateResolution found) =>
        template.Entries.Count == 0
            ? template with { AllowedIps = [], Missed = [], RefreshedUtc = null }
            : template with { AllowedIps = found.AllowedIps, Missed = found.Missed, RefreshedUtc = _time.GetUtcNow() };

    // Counts the questions passed on to the name servers and the ones they answered.
    private sealed class Tally(IDnsUpstream inner) : IDnsUpstream
    {
        private int _asked;

        private int _answered;

        public async Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            Interlocked.Increment(ref _asked);
            var answer = await inner.AskAsync(question, stream, ct).ConfigureAwait(false);
            if (answer is not null)
            {
                Interlocked.Increment(ref _answered);
            }

            return answer;
        }

        // Returns why the names went unanswered, or null when the name servers answered them.
        public string? Silence(string? fault, string? way)
        {
            if (Volatile.Read(ref _asked) == 0)
            {
                return null;
            }

            if (fault is not null)
            {
                return fault;
            }

            return Volatile.Read(ref _answered) == 0 ? way ?? NoAnswer : null;
        }
    }
}
