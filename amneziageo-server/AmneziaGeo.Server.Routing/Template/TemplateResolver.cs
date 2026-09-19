using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Template;

/// <summary>
/// What one entry of a template came out as.
/// </summary>
/// <param name="Entry">The entry as the template keeps it.</param>
/// <param name="AllowedIps">The ranges the entry stands for.</param>
public sealed record TemplatePart(string Entry, IReadOnlyList<string> AllowedIps);

/// <summary>
/// What the entries of a template came out as.
/// </summary>
/// <param name="AllowedIps">The ranges the client file carries.</param>
/// <param name="Missed">The entries nothing was found for.</param>
/// <param name="Parts">What each entry gave on its own.</param>
public sealed record TemplateResolution(
    IReadOnlyList<string> AllowedIps,
    IReadOnlyList<string> Missed,
    IReadOnlyList<TemplatePart> Parts);

/// <summary>
/// Turns the entries of a template into ranges: geo keys over the databases, names through the name servers.
/// </summary>
public sealed class TemplateResolver
{
    /// <summary>
    /// The most names one pass asks about.
    /// </summary>
    public const int MaxNames = 4000;

    /// <summary>
    /// The longest one pass asks the name servers for.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    private const int Parallel = 16;

    private readonly IDnsUpstream _upstream;

    /// <summary>
    /// ctor
    /// </summary>
    public TemplateResolver(IDnsUpstream upstream)
    {
        _upstream = upstream;
    }

    /// <summary>
    /// Returns the ranges the entries stand for, what each of them gave and the ones that gave none.
    /// </summary>
    public async Task<TemplateResolution> ResolveAsync(
        IReadOnlyList<string> entries,
        GeoIndex index,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(index);

        var found = new Dictionary<string, List<AwgAllowedIp>>(StringComparer.Ordinal);
        var named = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (found.ContainsKey(entry))
            {
                continue;
            }

            var ranges = new List<AwgAllowedIp>();
            found[entry] = ranges;
            var rule = TemplateList.Rule(entry);
            if (rule is null)
            {
                continue;
            }

            if (rule.Kind == GeoRuleKind.Cidr)
            {
                ranges.Add(AwgAllowedIp.Parse(rule.Value));
            }
            else if (rule.Kind == GeoRuleKind.GeoIp)
            {
                ranges.AddRange(index.Cidrs(rule.Value).Select(Range).OfType<AwgAllowedIp>());
            }
            else
            {
                named[entry] = rule.Kind == GeoRuleKind.Domain ? [rule.Value] : Names(index.Domains(rule.Value));
            }
        }

        var answers = await AskAllAsync(Wanted(named), ct).ConfigureAwait(false);
        foreach (var (entry, names) in named)
        {
            found[entry].AddRange(names
                .SelectMany(name => Answered(answers, name))
                .Select(address => new AwgAllowedIp(address, Width(address))));
        }

        var kept = entries.Distinct(StringComparer.Ordinal).ToArray();

        return new TemplateResolution(
            Written(found.Values.SelectMany(ranges => ranges)),
            [.. kept.Where(entry => found[entry].Count == 0)],
            [.. kept.Select(entry => new TemplatePart(entry, Written(found[entry])))]);
    }

    private async Task<Dictionary<string, IReadOnlyList<IPAddress>>> AskAllAsync(string[] names, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(Parallel);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);
        var answers = await Task.WhenAll(names.Select(name => OneAsync(name, gate, budget.Token))).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var found = new Dictionary<string, IReadOnlyList<IPAddress>>(StringComparer.OrdinalIgnoreCase);
        for (var at = 0; at < names.Length; at++)
        {
            found[names[at]] = answers[at];
        }

        return found;
    }

    private async Task<IReadOnlyList<IPAddress>> OneAsync(string name, SemaphoreSlim gate, CancellationToken ct)
    {
        try
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return [];
        }

        try
        {
            return await DnsLookup.AskAsync(_upstream, name, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        finally
        {
            gate.Release();
        }
    }

    private static IReadOnlyList<string> Written(IEnumerable<AwgAllowedIp> ranges) =>
        [.. RangeMerge.Merge(ranges).Select(range => range.ToString())];

    private static string[] Wanted(Dictionary<string, IReadOnlyList<string>> named) =>
        [.. named.Values.SelectMany(names => names).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxNames)];

    private static IReadOnlyList<IPAddress> Answered(Dictionary<string, IReadOnlyList<IPAddress>> answers, string name) =>
        answers.TryGetValue(name, out var found) ? found : [];

    private static IReadOnlyList<string> Names(IReadOnlyList<GeoDomain> domains) =>
    [
        .. domains
            .Where(domain => domain.Kind is GeoDomainKind.Domain or GeoDomainKind.Full)
            .Select(domain => domain.Value.ToLowerInvariant())
    ];

    private static AwgAllowedIp? Range(string cidr) => AwgAllowedIp.TryParse(cidr, out var range) ? range : null;

    private static byte Width(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)128 : (byte)32;
}
