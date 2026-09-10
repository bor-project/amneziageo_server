using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Template;

/// <summary>
/// What the entries of a template came out as.
/// </summary>
/// <param name="AllowedIps">The ranges the client file carries.</param>
/// <param name="Missed">The entries nothing was found for.</param>
public sealed record TemplateResolution(IReadOnlyList<string> AllowedIps, IReadOnlyList<string> Missed);

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

    private static readonly DnsRecordType[] Kinds = [DnsRecordType.A, DnsRecordType.Aaaa];

    private readonly IDnsUpstream _upstream;

    /// <summary>
    /// ctor
    /// </summary>
    public TemplateResolver(IDnsUpstream upstream)
    {
        _upstream = upstream;
    }

    /// <summary>
    /// Returns the ranges the entries stand for and the entries that gave none.
    /// </summary>
    public async Task<TemplateResolution> ResolveAsync(
        IReadOnlyList<string> entries,
        GeoIndex index,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(index);

        var ranges = new List<AwgAllowedIp>();
        var empty = new HashSet<string>(StringComparer.Ordinal);
        var named = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var rule = RouteRules.Target(entry);
            if (rule is null)
            {
                empty.Add(entry);
            }
            else if (rule.Kind == GeoRuleKind.Cidr)
            {
                ranges.Add(AwgAllowedIp.Parse(rule.Value));
            }
            else if (rule.Kind == GeoRuleKind.GeoIp)
            {
                var found = index.Cidrs(rule.Value).Select(Range).OfType<AwgAllowedIp>().ToArray();
                if (found.Length == 0)
                {
                    empty.Add(entry);
                }

                ranges.AddRange(found);
            }
            else
            {
                named[entry] = rule.Kind == GeoRuleKind.Domain ? [rule.Value] : Names(index.Domains(rule.Value));
            }
        }

        var answers = await AskAllAsync(Wanted(named), ct).ConfigureAwait(false);
        foreach (var (entry, names) in named)
        {
            var found = names.SelectMany(name => Answered(answers, name)).ToArray();
            if (found.Length == 0)
            {
                empty.Add(entry);
            }

            ranges.AddRange(found.Select(address => new AwgAllowedIp(address, Width(address))));
        }

        return new TemplateResolution(
            [.. RangeMerge.Merge(ranges).Select(range => range.ToString())],
            [.. entries.Where(empty.Contains)]);
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
            return await AskAsync(name, ct).ConfigureAwait(false);
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

    private async Task<IReadOnlyList<IPAddress>> AskAsync(string name, CancellationToken ct)
    {
        var ascii = Ascii(name);
        if (ascii is null)
        {
            return [];
        }

        var found = new List<IPAddress>();
        foreach (var kind in Kinds)
        {
            var answer = await _upstream.AskAsync(DnsQuestion.Packet(ascii, kind, DnsQuestion.Id()), false, ct)
                .ConfigureAwait(false);
            if (answer is not null && DnsMessage.Read(answer) is { Code: 0 } message)
            {
                found.AddRange(message.Answers
                    .Where(record => record.Type == kind)
                    .Select(record => record.Address)
                    .OfType<IPAddress>());
            }
        }

        return found;
    }

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

    private static string? Ascii(string name)
    {
        try
        {
            return new IdnMapping().GetAscii(name.Trim('.'));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static AwgAllowedIp? Range(string cidr) => AwgAllowedIp.TryParse(cidr, out var range) ? range : null;

    private static byte Width(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)128 : (byte)32;
}
