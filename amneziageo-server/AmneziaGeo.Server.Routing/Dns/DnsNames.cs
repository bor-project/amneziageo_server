using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Tells which rules a name belongs to.
/// </summary>
public sealed class DnsNames
{
    private readonly IReadOnlyList<Leg> _legs;

    private DnsNames(IReadOnlyList<Leg> legs) => _legs = legs;

    /// <summary>
    /// How many rules match by name.
    /// </summary>
    public int Count => _legs.Count;

    /// <summary>
    /// Builds the matchers out of the rules the host was given.
    /// </summary>
    public static DnsNames Build(RoutePlan? plan)
    {
        if (plan is null)
        {
            return new DnsNames([]);
        }

        var legs = plan.Legs
            .Where(leg => leg.IsOnHost && leg.Domains.Count > 0)
            .Select(leg => new Leg(leg.Rule.Id, new DomainMatcher(leg.Domains)))
            .ToArray();

        return new DnsNames(legs);
    }

    /// <summary>
    /// Returns the rules the name falls under.
    /// </summary>
    public IReadOnlyList<long> Match(string name)
    {
        if (_legs.Count == 0 || string.IsNullOrEmpty(name))
        {
            return [];
        }

        return [.. _legs.Where(leg => leg.Matcher.Matches(name)).Select(leg => leg.Id)];
    }

    private sealed record Leg(long Id, DomainMatcher Matcher);
}
