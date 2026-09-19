using System.Net;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// One name of a rule and the rule it fills the set of.
/// </summary>
/// <param name="Rule">The number of the rule.</param>
/// <param name="Name">The name to ask about.</param>
public sealed record DnsWarmName(long Rule, string Name);

/// <summary>
/// Asks about the names of the rules before a client does.
/// </summary>
public static class DnsWarm
{
    /// <summary>
    /// How many names of one rule are asked about.
    /// </summary>
    public const int MaxPerRule = 256;

    private const int Parallel = 8;

    private static readonly TimeSpan Miss = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan MissTop = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Returns how long a name that answered stands before it is asked about again.
    /// </summary>
    public static TimeSpan Rest(TimeSpan lifetime) => lifetime > TimeSpan.Zero ? lifetime / 2 : Miss;

    /// <summary>
    /// Returns how long a name that did not answer waits before it is asked about again.
    /// </summary>
    public static TimeSpan Again(int misses, TimeSpan lifetime)
    {
        var wait = Miss * (1 << (Math.Clamp(misses, 1, 6) - 1));
        var rest = Rest(lifetime);
        var top = rest < MissTop ? rest : MissTop;

        return wait < top ? wait : top;
    }

    /// <summary>
    /// Returns the names the rules of a plan match by, rule by rule.
    /// </summary>
    public static IReadOnlyList<DnsWarmName> Names(RoutePlan? plan)
    {
        if (plan is null)
        {
            return [];
        }

        return
        [
            .. plan.Legs
                .Where(leg => leg.IsOnHost && leg.Domains.Count > 0)
                .SelectMany(leg => leg.Domains
                    .Where(domain => domain.Kind is GeoDomainKind.Domain or GeoDomainKind.Full)
                    .Take(MaxPerRule)
                    .Select(domain => new DnsWarmName(leg.Rule.Id, domain.Value.ToLowerInvariant())))
                .Distinct()
        ];
    }

    /// <summary>
    /// Returns what the names answered with, asked through the upstream given.
    /// </summary>
    public static async Task<IReadOnlyList<(DnsWarmName Name, IPAddress Address)>> AskAsync(
        IDnsUpstream upstream,
        IReadOnlyList<DnsWarmName> names,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(names);

        using var gate = new SemaphoreSlim(Parallel);
        var answers = await Task.WhenAll(names.Select(one => OneAsync(upstream, one, gate, ct))).ConfigureAwait(false);

        return [.. answers.SelectMany(found => found)];
    }

    private static async Task<IReadOnlyList<(DnsWarmName Name, IPAddress Address)>> OneAsync(
        IDnsUpstream upstream,
        DnsWarmName name,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var found = await DnsLookup.AskAsync(upstream, name.Name, ct).ConfigureAwait(false);

            return [.. found.Select(address => (name, address))];
        }
        finally
        {
            gate.Release();
        }
    }
}
