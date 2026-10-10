using System.Globalization;
using System.Text;
using AmneziaGeo.Server.Routing.Access;
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

    private const uint RouteMarks = 0x0000_FFFF;

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

        Https(text, plan);

        text.Append("\tchain prerouting {\n");
        text.Append("\t\ttype filter hook prerouting priority mangle; policy accept;\n");
        if (plan.Inbound.Count > 0)
        {
            text.Append("\t\tiifname != { ").Append(Quoted(plan.Inbound)).Append(" } accept\n");
            text.Append("\t\tct state invalid drop\n");
            text.Append("\t\tct mark and ").Append(Hex8(RouteMarks)).Append(" != 0x00000000 meta mark set ct mark and ");
            text.Append(Hex8(RouteMarks)).Append(" accept\n");
            text.Append("\t\tct state != new accept\n");
            text.Append("\t\tjump decide\n");
            text.Append(plan.Journal is null
                ? "\t\tmeta mark != 0x00000000 ct mark set meta mark\n"
                : $"\t\tct mark set meta mark or {Hex8(AccessDefaults.Watching)}\n");
        }

        text.Append("\t}\n\n\tchain decide {\n");
        foreach (var line in Guard(plan).Concat(live.SelectMany(leg => Lines(leg, plan.Journal))))
        {
            text.Append("\t\t").Append(line).Append('\n');
        }

        if (plan.Journal is { } group)
        {
            text.Append("\t\t").Append(AccessTag.Statement(AccessTag.Nothing, group)).Append('\n');
        }

        text.Append("\t}\n");
        Redirect(text, plan);
        Answers(text, plan);
        text.Append("}\n");

        return text.ToString();
    }

    /// <summary>
    /// Returns the guard lines of a plan, none while no rule on the host matches by name.
    /// </summary>
    public static IReadOnlyList<string> Guard(RoutePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.HasNames ? Guard(plan.Dns, plan.Journal) : [];
    }

    /// <summary>
    /// Returns the lines that keep the clients off the name servers they carry their own way.
    /// </summary>
    public static IReadOnlyList<string> Guard(DnsSettings dns, ushort? journal = null)
    {
        ArgumentNullException.ThrowIfNull(dns);

        if (!dns.IsEnabled)
        {
            return [];
        }

        var lines = new List<string>();
        if (dns.BlockDot)
        {
            lines.Add("meta l4proto { tcp, udp } th dport 853 " + Log(AccessTag.Dot, journal) + "drop");
        }

        if (dns.BlockDoh)
        {
            var log = Log(AccessTag.Doh, journal);
            lines.Add($"ip daddr @{HttpsSet(false)} meta l4proto {{ tcp, udp }} th dport 443 {log}drop");
            lines.Add($"ip6 daddr @{HttpsSet(true)} meta l4proto {{ tcp, udp }} th dport 443 {log}drop");
        }

        return lines;
    }

    /// <summary>
    /// Returns the lines one rule takes in the decision chain.
    /// </summary>
    public static IReadOnlyList<string> Lines(RouteLeg leg) => Lines(leg, null);

    /// <summary>
    /// Returns the lines one rule takes in the decision chain, handing what it decides to a log group.
    /// </summary>
    public static IReadOnlyList<string> Lines(RouteLeg leg, ushort? journal)
    {
        ArgumentNullException.ThrowIfNull(leg);

        if (leg.Rule.Targets.Count == 0 && !leg.IsBySource)
        {
            return Plain(leg, journal);
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

            var tail = Ports(leg.Rule) + Verdict(leg, six, journal);
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

    private static IReadOnlyList<string> Plain(RouteLeg leg, ushort? journal)
    {
        var head = Inbound(leg.Rule);
        if (!Sticky(leg))
        {
            return [head + Ports(leg.Rule) + Verdict(leg, false, journal)];
        }

        return
        [
            head + "meta nfproto ipv4 " + Ports(leg.Rule) + Verdict(leg, false, journal),
            head + "meta nfproto ipv6 " + Ports(leg.Rule) + Verdict(leg, true, journal),
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

    private static void Https(StringBuilder text, RoutePlan plan)
    {
        if (!plan.HasNames || !plan.Dns.IsEnabled || !plan.Dns.BlockDoh)
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

    private static void Answers(StringBuilder text, RoutePlan plan)
    {
        if (plan.Journal is not { } group || plan.Inbound.Count == 0)
        {
            return;
        }

        var log = AccessTag.Statement(AccessTag.Reply, group);
        var watching = Hex8(AccessDefaults.Watching);
        var open = Hex8(AccessDefaults.Watching | AccessDefaults.Settled);
        var answered = Hex8(AccessDefaults.Answered);
        var settled = Hex8(AccessDefaults.Answered | AccessDefaults.Settled);
        text.Append("\n\tchain answer {\n");
        text.Append("\t\ttype filter hook postrouting priority mangle; policy accept;\n");
        text.Append("\t\toifname != { ").Append(Quoted(plan.Inbound)).Append(" } accept\n");
        text.Append("\t\tct direction original accept\n");
        text.Append($"\t\tct mark and {open} != {watching} accept\n");
        text.Append("\t\ticmp type destination-unreachable icmp code 4 accept\n");
        text.Append("\t\ticmpv6 type packet-too-big accept\n");
        text.Append($"\t\ttcp flags & (fin | rst | psh) != 0 ct mark set ct mark or {settled} {log} accept\n");
        text.Append($"\t\tmeta l4proto tcp ct mark and {answered} != 0x00000000 accept\n");
        text.Append($"\t\tmeta l4proto tcp ct mark set ct mark or {answered} {log} accept\n");
        text.Append($"\t\tct mark set ct mark or {settled} {log}\n");
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

    private static string Verdict(RouteLeg leg, bool six, ushort? journal)
    {
        var log = Log(AccessTag.Of(leg), journal);
        if (leg.IsBlock || leg.IsHeld)
        {
            return log + "drop";
        }

        if (leg.IsDirect)
        {
            return log + "return";
        }

        return leg.Exit.IsSpread
            ? "meta mark set " + Pick(leg.Exit, six) + " map { " + Map(leg.Exit) + " } " + log + "return"
            : "meta mark set " + Hex(leg.Exit.Mark) + " " + log + "return";
    }

    private static string Log(string prefix, ushort? journal) =>
        journal is { } group ? AccessTag.Statement(prefix, group) + " " : string.Empty;

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

    private static string Hex8(uint mark) => "0x" + mark.ToString("x8", CultureInfo.InvariantCulture);

    private static string Tag(long id) =>
        id < 0 ? "b" + (-id).ToString(CultureInfo.InvariantCulture) : id.ToString(CultureInfo.InvariantCulture);

    private static string Quoted(IReadOnlyList<string> names) =>
        string.Join(", ", names.Select(name => $"\"{name}\""));
}
