using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Client;
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
/// <param name="Sources4">The IPv4 client ranges the rule matches by, the addresses of the named clients among them.</param>
/// <param name="Sources6">The IPv6 client ranges the rule matches by, the addresses of the named clients among them.</param>
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
    /// Tells whether the rule drops what it matches while its way out carries nothing.
    /// </summary>
    public bool IsHeld => Rule.IsEnabled && Rule.HoldsWhenDown && Fault is { IsWayGone: true };

    /// <summary>
    /// Tells whether the rule is written into the ruleset, either sending traffic out or holding it back.
    /// </summary>
    public bool IsOnHost => IsLive || IsHeld;

    /// <summary>
    /// Tells whether the rule drops what it matches.
    /// </summary>
    public bool IsBlock => Rule.Action == RouteAction.Block;

    /// <summary>
    /// Tells whether the rule sends what it matches out the way the host sends its own.
    /// </summary>
    public bool IsDirect => Rule.Action == RouteAction.Direct;

    /// <summary>
    /// Tells whether the rule matches by where the traffic comes from.
    /// </summary>
    public bool IsBySource => Rule.Sources.Count > 0 || Rule.Clients.Count > 0;
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
    /// The ways out of a panel that holds no outbound.
    /// </summary>
    public static readonly RouteWays None = new([], [], null);

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
    /// The outbounds and balancers the rules were expanded over.
    /// </summary>
    public RouteWays Ways { get; init; } = RouteWays.None;

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
        IReadOnlySet<string>? alive = null,
        IReadOnlyList<TunnelClient>? clients = null,
        RouteBasic? basic = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(outbounds);
        ArgumentNullException.ThrowIfNull(inbound);

        var ways = new RouteWays(outbounds, balancers ?? [], alive);
        var interfaces = inbound.Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var known = new RouteKnown(clients ?? [], interfaces);
        var legs = (basic ?? RouteBasic.Empty).Rules()
            .Concat(rules.OrderBy(rule => rule.Position))
            .Select(rule => Leg(rule, ways, index, known))
            .ToArray();

        return new RoutePlan(legs, interfaces) { Dns = dns ?? DnsDefaults.Settings, Ways = ways };
    }

    private static RouteLeg Leg(RouteRule rule, RouteWays ways, GeoIndex index, RouteKnown known)
    {
        var targets = rule.Targets.Select(RouteRules.Target).OfType<GeoRule>().ToArray();
        var found = GeoMaterializer.Materialize(targets, index);
        var cidrs4 = found.Cidrs.Where(cidr => !cidr.Contains(':', StringComparison.Ordinal)).Distinct().ToArray();
        var cidrs6 = found.Cidrs.Where(cidr => cidr.Contains(':', StringComparison.Ordinal)).Distinct().ToArray();
        var sources = rule.Sources
            .Select(source => RouteRules.Source(source, out var range) ? range : null)
            .Concat(known.Addresses(rule.Clients))
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
            Fault(rule, way.Fault, cidrs4.Length + cidrs6.Length + found.Domains.Count, known.Missing(rule)));
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
            if (!outbound.IsEnabled)
            {
                return (RouteExit.None, new RouteFault("outbound-off", $"the outbound '{name}' is turned off"));
            }

            return ways.IsAlive(name)
                ? (RouteExit.One(outbound.Mark), null)
                : (RouteExit.None, new RouteFault("outbound-down", $"the outbound '{name}' carries nothing"));
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

        return (new RouteExit(balancer.Strategy, marks) { Seed = balancer.Seed }, null);
    }

    private static IEnumerable<string> Family(IReadOnlyList<AwgAllowedIp> ranges, AddressFamily family) =>
        ranges.Where(range => range.Address.AddressFamily == family).Select(range => range.ToString()).Distinct();

    private static RouteFault? Fault(RouteRule rule, RouteFault? way, int matches, RouteFault? missing)
    {
        if ((RouteRules.Check(rule) ?? missing) is { } broken)
        {
            return broken;
        }

        return rule.Targets.Count > 0 && matches == 0
            ? new RouteFault("empty-target", "the geo databases carry nothing for what the rule matches by")
            : way;
    }
}

/// <summary>
/// The clients and the interfaces the rules name.
/// </summary>
/// <param name="Clients">Every client the panel holds, the devices among them.</param>
/// <param name="Interfaces">The interfaces the clients arrive on.</param>
public sealed record RouteKnown(IReadOnlyList<TunnelClient> Clients, IReadOnlyList<string> Interfaces)
{
    /// <summary>
    /// Returns the addresses of the named clients and of their devices.
    /// </summary>
    public IEnumerable<AwgAllowedIp?> Addresses(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var named = Clients.Where(client => Named(names, client.Name)).Select(client => client.Id).ToHashSet();

        return Clients
            .Where(client => named.Contains(client.Id) || (client.ParentId is { } parent && named.Contains(parent)))
            .SelectMany(client => client.Address)
            .Select(address => AwgAllowedIp.TryParse(address, out var range) ? range : null);
    }

    /// <summary>
    /// Returns why a rule names nothing the panel holds, or null when it names something.
    /// </summary>
    public RouteFault? Missing(RouteRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.Clients.Count > 0 && !Clients.Any(client => Named(rule.Clients, client.Name)))
        {
            return new RouteFault("unknown-client", "the panel holds none of the clients the rule names");
        }

        return rule.Inbounds.Count > 0 && !rule.Inbounds.Any(name => Interfaces.Contains(name, StringComparer.Ordinal))
            ? new RouteFault("unknown-inbound", "the panel holds none of the interfaces the rule names")
            : null;
    }

    private static bool Named(IReadOnlyList<string> names, string name) =>
        names.Contains(name, StringComparer.OrdinalIgnoreCase);
}
