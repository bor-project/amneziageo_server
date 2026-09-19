using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// The traffic the rules are tried against.
/// </summary>
public sealed record RouteQuery
{
    /// <summary>
    /// The name the traffic goes to, empty when it goes to a bare address.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The addresses the traffic goes to.
    /// </summary>
    public IReadOnlyList<IPAddress> Addresses { get; init; } = [];

    /// <summary>
    /// The port the traffic goes to, 0 when none is given.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// The port the traffic comes from, 0 when none is given.
    /// </summary>
    public int SourcePort { get; init; }

    /// <summary>
    /// The protocol the traffic carries.
    /// </summary>
    public string Protocol { get; init; } = RouteProtocol.Tcp;

    /// <summary>
    /// The addresses the traffic comes from.
    /// </summary>
    public IReadOnlyList<IPAddress> Sources { get; init; } = [];

    /// <summary>
    /// The interface the traffic comes in on, empty when it is not known.
    /// </summary>
    public string Inbound { get; init; } = string.Empty;
}

/// <summary>
/// How one rule took the traffic.
/// </summary>
/// <param name="Rule">The number of the rule.</param>
/// <param name="Name">The name of the rule.</param>
/// <param name="Outcome">Whether the rule matched, missed or was passed over.</param>
/// <param name="Reason">What decided the outcome.</param>
/// <param name="Detail">The address or the name the rule matched by, empty otherwise.</param>
public sealed record RouteStep(long Rule, string Name, string Outcome, string Reason, string Detail);

/// <summary>
/// Where the rules send the traffic.
/// </summary>
/// <param name="Verdict">What happens to the traffic.</param>
/// <param name="Guard">Which guard of the resolver drops the traffic, empty when none.</param>
/// <param name="Leg">The rule that matched, or null when none did.</param>
/// <param name="Steps">How each rule read before the decision took the traffic.</param>
public sealed record RouteVerdict(string Verdict, string Guard, RouteLeg? Leg, IReadOnlyList<RouteStep> Steps);

/// <summary>
/// Tries the rules the host carries against the traffic, the way the ruleset does.
/// </summary>
public static class RouteProbe
{
    /// <summary>
    /// The rule matched the traffic.
    /// </summary>
    public const string Match = "match";

    /// <summary>
    /// The rule did not match the traffic.
    /// </summary>
    public const string Miss = "miss";

    /// <summary>
    /// The rule is not on the host.
    /// </summary>
    public const string Skip = "skip";

    /// <summary>
    /// The traffic leaves through the outbound of the rule.
    /// </summary>
    public const string Out = "out";

    /// <summary>
    /// The traffic leaves the way the host sends its own.
    /// </summary>
    public const string Host = "host";

    /// <summary>
    /// The traffic is dropped by a rule.
    /// </summary>
    public const string Block = "block";

    /// <summary>
    /// The traffic is held back while the way out of the rule carries nothing.
    /// </summary>
    public const string Held = "held";

    /// <summary>
    /// The traffic is dropped by a guard of the resolver.
    /// </summary>
    public const string Guarded = "guard";

    /// <summary>
    /// Returns where the rules send the traffic.
    /// </summary>
    public static RouteVerdict Test(RoutePlan plan, RouteQuery query, Func<long, IPAddress, bool> inSet)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(inSet);

        if (Guard(plan.Dns, query) is { } guard)
        {
            return new RouteVerdict(Guarded, guard, null, []);
        }

        var named = query.Name.Length > 0 ? DnsNames.Build(plan).Match(query.Name).ToHashSet() : [];
        var steps = new List<RouteStep>();
        foreach (var leg in plan.Legs)
        {
            var step = Try(leg, query, named, inSet);
            steps.Add(step);
            if (step.Outcome == Match)
            {
                return new RouteVerdict(Verdict(leg), string.Empty, leg, steps);
            }
        }

        return new RouteVerdict(Host, string.Empty, null, steps);
    }

    /// <summary>
    /// Tells whether an address falls into one of the ranges.
    /// </summary>
    public static bool Within(IEnumerable<string> ranges, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(address);

        foreach (var text in ranges)
        {
            if (AwgAllowedIp.TryParse(text, out var range)
                && range.Address.AddressFamily == address.AddressFamily
                && new IPNetwork(range.Network().Address, range.Cidr).Contains(address))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Guard(DnsSettings dns, RouteQuery query)
    {
        if (!dns.IsEnabled)
        {
            return null;
        }

        if (dns.BlockDot && query.Port == 853)
        {
            return "dot";
        }

        var https = DnsDefaults.Https4.Concat(DnsDefaults.Https6).ToArray();

        return dns.BlockDoh && query.Port == 443 && query.Addresses.Any(address => Within(https, address))
            ? "doh"
            : null;
    }

    private static RouteStep Try(RouteLeg leg, RouteQuery query, HashSet<long> named, Func<long, IPAddress, bool> inSet)
    {
        if (!leg.IsOnHost)
        {
            return Step(leg, Skip, leg.Rule.IsEnabled ? leg.Fault?.Code ?? "off" : "off", string.Empty);
        }

        if (Missed(leg, query) is { } reason)
        {
            return Step(leg, Miss, reason, string.Empty);
        }

        if (leg.Rule.Targets.Count == 0)
        {
            return Step(leg, Match, "any", string.Empty);
        }

        var range = query.Addresses.FirstOrDefault(address => Within(Ranges(leg, address), address));
        if (range is not null)
        {
            return Step(leg, Match, "range", range.ToString());
        }

        if (named.Contains(leg.Rule.Id))
        {
            return Step(leg, Match, "name", query.Name);
        }

        var laid = leg.Domains.Count > 0
            ? query.Addresses.FirstOrDefault(address => inSet(leg.Rule.Id, address))
            : null;

        return laid is null ? Step(leg, Miss, "target", string.Empty) : Step(leg, Match, "resolved", laid.ToString());
    }

    private static string? Missed(RouteLeg leg, RouteQuery query)
    {
        var rule = leg.Rule;
        if (rule.Inbounds.Count > 0 && !rule.Inbounds.Contains(query.Inbound, StringComparer.Ordinal))
        {
            return "inbound";
        }

        if (leg.IsBySource && !query.Sources.Any(source => Within(leg.Sources4.Concat(leg.Sources6), source)))
        {
            return "source";
        }

        if (rule.Protocol != RouteProtocol.Any && rule.Protocol != query.Protocol)
        {
            return "protocol";
        }

        if (rule.Ports.Count > 0 && !Takes(rule.Ports, query.Port))
        {
            return "port";
        }

        return rule.SourcePorts.Count > 0 && !Takes(rule.SourcePorts, query.SourcePort) ? "source-port" : null;
    }

    private static IReadOnlyList<string> Ranges(RouteLeg leg, IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? leg.Cidrs6 : leg.Cidrs4;

    private static bool Takes(IReadOnlyList<string> ports, int port) =>
        port > 0 && ports.Any(text => RouteRules.Ports(text, out var low, out var high) && port >= low && port <= high);

    private static string Verdict(RouteLeg leg)
    {
        if (leg.IsHeld)
        {
            return Held;
        }

        if (leg.IsBlock)
        {
            return Block;
        }

        return leg.IsDirect ? Host : Out;
    }

    private static RouteStep Step(RouteLeg leg, string outcome, string reason, string detail) =>
        new(leg.Rule.Id, leg.Rule.Name, outcome, reason, detail);
}
