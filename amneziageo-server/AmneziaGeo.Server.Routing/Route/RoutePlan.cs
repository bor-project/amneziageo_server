using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// One rule with everything the host needs to carry it out.
/// </summary>
/// <param name="Rule">The rule as the panel holds it.</param>
/// <param name="Exit">The marks that send the traffic out, and how one of them is picked.</param>
/// <param name="Cidrs4">The IPv4 ranges the rule matches by.</param>
/// <param name="Cidrs6">The IPv6 ranges the rule matches by.</param>
/// <param name="Domains">The names the rule matches by.</param>
/// <param name="Sources4">The IPv4 client ranges the rule matches by.</param>
/// <param name="Sources6">The IPv6 client ranges the rule matches by.</param>
/// <param name="Fault">Why the rule stays off the host, or null when it goes on.</param>
public sealed record RouteLeg(
    RouteRule Rule,
    RouteExit Exit,
    IReadOnlyList<string> Cidrs4,
    IReadOnlyList<string> Cidrs6,
    IReadOnlyList<GeoDomain> Domains,
    IReadOnlyList<string> Sources4,
    IReadOnlyList<string> Sources6,
    RouteFault? Fault)
{
    /// <summary>
    /// The mark the traffic of the rule carries.
    /// </summary>
    public uint Mark => Exit.Mark;

    /// <summary>
    /// Tells whether the rule goes on the host.
    /// </summary>
    public bool IsLive => Rule.IsEnabled && Fault is null;

    /// <summary>
    /// Tells whether the rule drops what it matches.
    /// </summary>
    public bool IsBlock => Rule.Action == RouteAction.Block;
}

/// <summary>
/// The outbounds and balancers the rules leave through, with the ones carrying traffic marked.
/// </summary>
/// <param name="Outbounds">Every outbound the panel holds.</param>
/// <param name="Balancers">Every balancer the panel holds.</param>
/// <param name="Alive">The outbounds that carry traffic, or null when the host was not read yet.</param>
public sealed record RouteWays(
    IReadOnlyList<OutboundConfig> Outbounds,
    IReadOnlyList<Balancer> Balancers,
    IReadOnlySet<string>? Alive)
{
    /// <summary>
    /// Returns the outbound under a name, or null when the panel holds none.
    /// </summary>
    public OutboundConfig? Outbound(string name) =>
        Outbounds.FirstOrDefault(one => string.Equals(one.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Returns the balancer under a name, or null when the panel holds none.
    /// </summary>
    public Balancer? Balancer(string name) =>
        Balancers.FirstOrDefault(one => string.Equals(one.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Tells whether an outbound carries traffic.
    /// </summary>
    public bool IsAlive(string name) => Alive is null || Alive.Contains(name);
}

/// <summary>
/// Every rule the panel holds, expanded over the geo databases and the outbounds.
/// </summary>
/// <param name="Legs">The rules in the order they are read in.</param>
/// <param name="Inbound">The interfaces the clients arrive on.</param>
public sealed record RoutePlan(IReadOnlyList<RouteLeg> Legs, IReadOnlyList<string> Inbound)
{
    /// <summary>
    /// The resolver the clients of the tunnels are given.
    /// </summary>
    public DnsSettings Dns { get; init; } = DnsDefaults.Settings;

    /// <summary>
    /// Expands the rules over the geo index, the outbounds and the balancers the panel holds.
    /// </summary>
    public static RoutePlan Build(
        IReadOnlyList<RouteRule> rules,
        IReadOnlyList<OutboundConfig> outbounds,
        GeoIndex index,
        IReadOnlyList<string> inbound,
        DnsSettings? dns = null,
        IReadOnlyList<Balancer>? balancers = null,
        IReadOnlySet<string>? alive = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(outbounds);
        ArgumentNullException.ThrowIfNull(inbound);

        var ways = new RouteWays(outbounds, balancers ?? [], alive);
        var legs = rules.OrderBy(rule => rule.Position).Select(rule => Leg(rule, ways, index)).ToArray();

        var interfaces = inbound.Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

        return new RoutePlan(legs, interfaces) { Dns = dns ?? DnsDefaults.Settings };
    }

    private static RouteLeg Leg(RouteRule rule, RouteWays ways, GeoIndex index)
    {
        var targets = rule.Targets.Select(RouteRules.Target).OfType<GeoRule>().ToArray();
        var found = GeoMaterializer.Materialize(targets, index);
        var cidrs4 = found.Cidrs.Where(cidr => !cidr.Contains(':', StringComparison.Ordinal)).Distinct().ToArray();
        var cidrs6 = found.Cidrs.Where(cidr => cidr.Contains(':', StringComparison.Ordinal)).Distinct().ToArray();
        var sources = rule.Sources
            .Select(source => RouteRules.Source(source, out var range) ? range : null)
            .OfType<AwgAllowedIp>()
            .ToArray();

        var way = Way(rule, ways);

        return new RouteLeg(
            rule,
            way.Exit,
            cidrs4,
            cidrs6,
            found.Domains,
            [.. Family(sources, AddressFamily.InterNetwork)],
            [.. Family(sources, AddressFamily.InterNetworkV6)],
            Fault(rule, way.Fault, cidrs4.Length + cidrs6.Length + found.Domains.Count));
    }

    private static (RouteExit Exit, RouteFault? Fault) Way(RouteRule rule, RouteWays ways)
    {
        if (rule.Action != RouteAction.Out)
        {
            return (RouteExit.None, null);
        }

        var name = rule.Outbound.Trim();
        if (ways.Outbound(name) is { } outbound)
        {
            return outbound.IsEnabled
                ? (RouteExit.One(outbound.Mark), null)
                : (RouteExit.None, new RouteFault("outbound-off", $"the outbound '{name}' is turned off"));
        }

        return ways.Balancer(name) is { } balancer
            ? Spread(balancer, ways)
            : (RouteExit.None, new RouteFault("unknown-outbound", $"there is no outbound called '{name}'"));
    }

    private static (RouteExit Exit, RouteFault? Fault) Spread(Balancer balancer, RouteWays ways)
    {
        if (!balancer.IsEnabled)
        {
            return (RouteExit.None, new RouteFault("balancer-off", $"the balancer '{balancer.Name}' is turned off"));
        }

        var members = balancer.Members
            .Select(ways.Outbound)
            .OfType<OutboundConfig>()
            .Where(member => member.IsEnabled && ways.IsAlive(member.Name))
            .ToArray();

        if (members.Length == 0)
        {
            var message = $"nothing the balancer '{balancer.Name}' picks from carries traffic";

            return (RouteExit.None, new RouteFault("no-live-member", message));
        }

        var marks = BalanceStrategy.Spreads(balancer.Strategy)
            ? members.Select(member => member.Mark).ToArray()
            : [members[0].Mark];

        return (new RouteExit(balancer.Strategy, marks), null);
    }

    private static IEnumerable<string> Family(IReadOnlyList<AwgAllowedIp> ranges, AddressFamily family) =>
        ranges.Where(range => range.Address.AddressFamily == family).Select(range => range.ToString()).Distinct();

    private static RouteFault? Fault(RouteRule rule, RouteFault? way, int matches)
    {
        if (RouteRules.Check(rule) is { } broken)
        {
            return broken;
        }

        return rule.Targets.Count > 0 && matches == 0
            ? new RouteFault("empty-target", "the geo databases carry nothing for what the rule matches by")
            : way;
    }
}
