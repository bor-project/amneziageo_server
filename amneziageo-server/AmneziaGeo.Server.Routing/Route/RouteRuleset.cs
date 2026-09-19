using System.Globalization;
using System.Text;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// Writes the firewall ruleset that marks the traffic the rules match.
/// </summary>
public static class RouteRuleset
{
    /// <summary>
    /// The firewall table the routing rules live in.
    /// </summary>
    public const string TableName = "amneziageo_rt";

    /// <summary>
    /// Returns the name of the set that holds the ranges of a rule.
    /// </summary>
    public static string RangeSet(long id, bool six) => $"r{Tag(id)}v{(six ? 6 : 4)}";

    /// <summary>
    /// Returns the name of the set the resolver puts the addresses of a rule into.
    /// </summary>
    public static string NameSet(long id, bool six) => $"n{Tag(id)}v{(six ? 6 : 4)}";

    /// <summary>
    /// Returns the rule a set of the resolver belongs to, or null when the set is not one.
    /// </summary>
    public static long? NameSetRule(string set)
    {
        ArgumentNullException.ThrowIfNull(set);

        if (set.Length <= 3 || set[0] != 'n' || set[^2] != 'v')
        {
            return null;
        }

        var body = set.AsSpan(1, set.Length - 3);
        var basic = body.Length > 1 && body[0] == 'b';
        var digits = basic ? body[1..] : body;
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var rule))
        {
            return null;
        }

        return basic ? -rule : rule;
    }

    /// <summary>
    /// Returns the name of the set that holds the name servers answering over HTTPS.
    /// </summary>
    public static string HttpsSet(bool six) => six ? "doh6" : "doh4";

    /// <summary>
    /// Returns the ruleset that carries out the rules the panel holds.
    /// </summary>
    public static string Text(RoutePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var live = plan.Legs.Where(leg => leg.IsOnHost).ToArray();
        var text = new StringBuilder();
        text.Append("table inet ").Append(TableName).Append('\n');
        text.Append("delete table inet ").Append(TableName).Append('\n');
        text.Append("table inet ").Append(TableName).Append(" {\n");
        foreach (var leg in live)
        {
            Sets(text, leg, plan.Dns);
        }

        Https(text, plan.Dns);

        text.Append("\tchain prerouting {\n");
        text.Append("\t\ttype filter hook prerouting priority mangle; policy accept;\n");
        if (plan.Inbound.Count > 0)
        {
            text.Append("\t\tiifname != { ").Append(Quoted(plan.Inbound)).Append(" } accept\n");
            text.Append("\t\tct state invalid drop\n");
            text.Append("\t\tct mark != 0x00000000 meta mark set ct mark accept\n");
            text.Append("\t\tct state != new accept\n");
            text.Append("\t\tjump decide\n");
            text.Append("\t\tmeta mark != 0x00000000 ct mark set meta mark\n");
        }

        text.Append("\t}\n\n\tchain decide {\n");
        foreach (var line in Guard(plan.Dns).Concat(live.SelectMany(Lines)))
        {
            text.Append("\t\t").Append(line).Append('\n');
        }

        text.Append("\t}\n");
        Redirect(text, plan);
        text.Append("}\n");

        return text.ToString();
    }

    /// <summary>
    /// Returns the lines that keep the clients off the name servers they carry their own way.
    /// </summary>
    public static IReadOnlyList<string> Guard(DnsSettings dns)
    {
        ArgumentNullException.ThrowIfNull(dns);

        if (!dns.IsEnabled)
        {
            return [];
        }

        var lines = new List<string>();
        if (dns.BlockDot)
        {
            lines.Add("meta l4proto { tcp, udp } th dport 853 drop");
        }

        if (dns.BlockDoh)
        {
            lines.Add($"ip daddr @{HttpsSet(false)} meta l4proto {{ tcp, udp }} th dport 443 drop");
            lines.Add($"ip6 daddr @{HttpsSet(true)} meta l4proto {{ tcp, udp }} th dport 443 drop");
        }

        return lines;
    }

    /// <summary>
    /// Returns the lines one rule takes in the decision chain.
    /// </summary>
    public static IReadOnlyList<string> Lines(RouteLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);

        if (leg.Rule.Targets.Count == 0 && !leg.IsBySource)
        {
            return Plain(leg);
        }

        var lines = new List<string>();
        foreach (var six in new[] { false, true })
        {
            var family = six ? "ip6" : "ip";
            var sources = six ? leg.Sources6 : leg.Sources4;
            if (leg.IsBySource && sources.Count == 0)
            {
                continue;
            }

            var tail = Ports(leg.Rule) + Verdict(leg, six);
            var head = Inbound(leg.Rule)
                + (sources.Count > 0 ? $"{family} saddr {{ {string.Join(", ", sources)} }} " : string.Empty);
            foreach (var set in Sets(leg, six))
            {
                lines.Add($"{head}{family} daddr @{set} {tail}");
            }

            if (leg.Rule.Targets.Count == 0)
            {
                lines.Add(head + tail);
            }
        }

        return lines;
    }

    private static IReadOnlyList<string> Plain(RouteLeg leg)
    {
        var head = Inbound(leg.Rule);
        if (!Sticky(leg))
        {
            return [head + Ports(leg.Rule) + Verdict(leg, false)];
        }

        return
        [
            head + "meta nfproto ipv4 " + Ports(leg.Rule) + Verdict(leg, false),
            head + "meta nfproto ipv6 " + Ports(leg.Rule) + Verdict(leg, true),
        ];
    }

    private static bool Sticky(RouteLeg leg) =>
        !leg.IsBlock && leg.Exit.IsSpread && leg.Exit.Strategy == BalanceStrategy.Sticky;

    private static IEnumerable<string> Sets(RouteLeg leg, bool six)
    {
        if ((six ? leg.Cidrs6 : leg.Cidrs4).Count > 0)
        {
            yield return RangeSet(leg.Rule.Id, six);
        }

        if (leg.Domains.Count > 0)
        {
            yield return NameSet(leg.Rule.Id, six);
        }
    }

    private static void Sets(StringBuilder text, RouteLeg leg, DnsSettings dns)
    {
        Range(text, RangeSet(leg.Rule.Id, false), "ipv4_addr", leg.Cidrs4);
        Range(text, RangeSet(leg.Rule.Id, true), "ipv6_addr", leg.Cidrs6);
        if (leg.Domains.Count == 0)
        {
            return;
        }

        Names(text, NameSet(leg.Rule.Id, false), "ipv4_addr", dns);
        Names(text, NameSet(leg.Rule.Id, true), "ipv6_addr", dns);
    }

    private static void Https(StringBuilder text, DnsSettings dns)
    {
        if (!dns.IsEnabled || !dns.BlockDoh)
        {
            return;
        }

        Range(text, HttpsSet(false), "ipv4_addr", DnsDefaults.Https4);
        Range(text, HttpsSet(true), "ipv6_addr", DnsDefaults.Https6);
    }

    private static void Redirect(StringBuilder text, RoutePlan plan)
    {
        if (!plan.Dns.IsEnabled || !plan.Dns.Intercept || plan.Inbound.Count == 0)
        {
            return;
        }

        text.Append("\n\tchain resolve {\n");
        text.Append("\t\ttype nat hook prerouting priority dstnat; policy accept;\n");
        text.Append("\t\tiifname != { ").Append(Quoted(plan.Inbound)).Append(" } accept\n");
        text.Append("\t\tmeta l4proto { tcp, udp } th dport 53 redirect to :");
        text.Append(plan.Dns.Port.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("\t}\n");
    }

    private static void Range(StringBuilder text, string name, string type, IReadOnlyList<string> ranges)
    {
        if (ranges.Count == 0)
        {
            return;
        }

        text.Append("\tset ").Append(name).Append(" {\n");
        text.Append("\t\ttype ").Append(type).Append('\n');
        text.Append("\t\tflags interval\n");
        text.Append("\t\tauto-merge\n");
        text.Append("\t\telements = { ").Append(string.Join(", ", ranges)).Append(" }\n");
        text.Append("\t}\n\n");
    }

    private static void Names(StringBuilder text, string name, string type, DnsSettings dns)
    {
        text.Append("\tset ").Append(name).Append(" {\n");
        text.Append("\t\ttype ").Append(type).Append('\n');
        text.Append("\t\tflags timeout\n");
        text.Append("\t\ttimeout ").Append(dns.NameMinutes.ToString(CultureInfo.InvariantCulture)).Append("m\n");
        text.Append("\t}\n\n");
    }

    private static string Inbound(RouteRule rule) =>
        rule.Inbounds.Count == 0 ? string.Empty : $"iifname {{ {Quoted(rule.Inbounds)} }} ";

    private static string Ports(RouteRule rule)
    {
        var from = string.Join(", ", rule.SourcePorts.Select(port => port.Trim()));
        var to = string.Join(", ", rule.Ports.Select(port => port.Trim()));
        if (rule.Protocol == RouteProtocol.Any)
        {
            return from.Length == 0 && to.Length == 0
                ? string.Empty
                : "meta l4proto { tcp, udp } " + Pair("th", from, to);
        }

        return from.Length == 0 && to.Length == 0
            ? $"meta l4proto {rule.Protocol} "
            : Pair(rule.Protocol, from, to);
    }

    private static string Pair(string head, string from, string to) =>
        (from.Length > 0 ? $"{head} sport {{ {from} }} " : string.Empty)
        + (to.Length > 0 ? $"{head} dport {{ {to} }} " : string.Empty);

    private static string Verdict(RouteLeg leg, bool six)
    {
        if (leg.IsBlock || leg.IsHeld)
        {
            return "drop";
        }

        if (leg.IsDirect)
        {
            return "return";
        }

        return leg.Exit.IsSpread
            ? "meta mark set " + Pick(leg.Exit, six) + " map { " + Map(leg.Exit) + " } return"
            : "meta mark set " + Hex(leg.Exit.Mark) + " return";
    }

    private static string Pick(RouteExit exit, bool six)
    {
        var count = exit.Marks.Count.ToString(CultureInfo.InvariantCulture);

        return exit.Strategy == BalanceStrategy.Sticky
            ? $"jhash {(six ? "ip6" : "ip")} saddr mod {count} seed {Hex(exit.Seed)}"
            : $"numgen inc mod {count}";
    }

    private static string Map(RouteExit exit) =>
        string.Join(", ", exit.Marks.Select((mark, at) => $"{at.ToString(CultureInfo.InvariantCulture)} : {Hex(mark)}"));

    private static string Hex(uint mark) => "0x" + mark.ToString("x", CultureInfo.InvariantCulture);

    private static string Tag(long id) =>
        id < 0 ? "b" + (-id).ToString(CultureInfo.InvariantCulture) : id.ToString(CultureInfo.InvariantCulture);

    private static string Quoted(IReadOnlyList<string> names) =>
        string.Join(", ", names.Select(name => $"\"{name}\""));
}
