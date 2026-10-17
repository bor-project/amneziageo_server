using System.Globalization;
using System.Text;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Names;

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
    /// The counter of the connections of IPv4 the firewall hands over to the panel.
    /// </summary>
    public const string TakenCounter = "taken";

    /// <summary>
    /// The counter of the connections of IPv6 the firewall hands over to the panel.
    /// </summary>
    public const string TakenCounter6 = "taken6";

    private const uint RouteMarks = 0x0000_FFFF;

    private static readonly string Web = string.Join(", ", NameDefaults.Ports);

    private static readonly string Most = NameDefaults.MostTaken.ToString(CultureInfo.InvariantCulture);

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
    /// Returns the name of the set that holds the addresses the panel looked at.
    /// </summary>
    public static string SeenSet(bool six) => six ? "seen6" : "seen4";

    /// <summary>
    /// Returns the name of the set that holds the QUIC connections whose first packets the panel read.
    /// </summary>
    public static string ReadSet(bool six) => six ? "read6" : "read4";

    /// <summary>
    /// Returns the name of the set that holds the TCP connections whose first bytes wait to be read.
    /// </summary>
    public static string WaitSet(bool six) => six ? "wait6" : "wait4";

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
        Handed(text, plan);
        text.Append("\tchain prerouting {\n");
        text.Append("\t\ttype filter hook prerouting priority mangle; policy accept;\n");
        if (plan.Inbound.Count > 0)
        {
            text.Append("\t\tiifname != { ").Append(Quoted(plan.Inbound)).Append(" } accept\n");
            text.Append("\t\tct state invalid drop\n");
            Back(text, plan);
            text.Append("\t\tct mark and ").Append(Hex8(RouteMarks)).Append(" != 0x00000000 meta mark set ct mark and ");
            text.Append(Hex8(RouteMarks)).Append(" accept\n");
            text.Append("\t\tct state != new accept\n");
            text.Append("\t\tjump decide\n");
            text.Append(plan.Journal is null
                ? "\t\tmeta mark != 0x00000000 ct mark set meta mark\n"
                : $"\t\tct mark set meta mark or {Hex8(AccessDefaults.Watching)}\n");
            Asked(text, plan, live);
        }

        text.Append("\t}\n\n\tchain decide {\n");
        foreach (var line in Decide(plan, live))
        {
            text.Append("\t\t").Append(line).Append('\n');
        }

        if (plan.Journal is { } group)
        {
            text.Append("\t\t").Append(AccessTag.Statement(AccessTag.Nothing, group)).Append('\n');
        }

        text.Append("\t}\n");
        Redirect(text, plan);
        Door(text, plan);
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

    /// <summary>
    /// Returns the lines that stand right after a rule by name and deal with the TCP connections it may take: the
    /// firewall hands the first one to an address over to the panel where the panel takes them, and notes the rest,
    /// so that what their clients say first is shown to the panel.
    /// </summary>
    public static IReadOnlyList<string> Reading(RouteLeg leg, RouteReading? reading)
    {
        ArgumentNullException.ThrowIfNull(leg);

        if (reading is null || !leg.IsOnHost || leg.Domains.Count == 0 || leg.Rule.Protocol == RouteProtocol.Udp)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var six in new[] { false, true })
        {
            if (Head(leg, six) is not { } head)
            {
                continue;
            }

            var family = six ? "ip6" : "ip";
            var web = $"{head}{family} daddr != @{SeenSet(six)} tcp dport {{ {Web} }} " + Ports(leg.Rule, RouteProtocol.Tcp);
            if (reading.Port is { } port && (reading.Six || !six))
            {
                lines.Add(
                    web
                    + $"fib daddr type != local {family} daddr != {{ {string.Join(", ", six ? NameDefaults.Closed6 : NameDefaults.Closed4)} }} "
                    + $"limit rate {Most}/second burst {Most} packets counter name \"{(six ? TakenCounter6 : TakenCounter)}\" "
                    + $"tproxy {family} to {(six ? "[::1]" : "127.0.0.1")}:{port.ToString(CultureInfo.InvariantCulture)} "
                    + $"meta mark set {Hex8(NameDefaults.Taken)} ct mark set {Hex8(NameDefaults.Taken)} accept");
            }

            if (reading.Queue is not null)
            {
                lines.Add(web + $"update @{WaitSet(six)} {{ {family} saddr . tcp sport . {family} daddr }}");
            }
        }

        return lines;
    }

    /// <summary>
    /// Returns the lines that show the first packets of the QUIC connections a rule by name may take to the panel.
    /// </summary>
    public static IReadOnlyList<string> Asking(RouteLeg leg, RouteReading? reading)
    {
        ArgumentNullException.ThrowIfNull(leg);

        if (reading?.Queue is not { } queue || !leg.IsOnHost || leg.Domains.Count == 0 || leg.Rule.Protocol == RouteProtocol.Tcp)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var six in new[] { false, true })
        {
            if (Head(leg, six) is not { } head)
            {
                continue;
            }

            var family = six ? "ip6" : "ip";
            lines.Add(
                $"{head}{family} saddr . udp sport . {family} daddr != @{ReadSet(six)} udp dport 443 "
                + Ports(leg.Rule, RouteProtocol.Udp)
                + $"@th,64,8 & 0x80 == 0x80 queue num {queue.ToString(CultureInfo.InvariantCulture)} bypass");
        }

        return lines;
    }

    // Returns what a line of a rule starts with in one family, or null when the rule names clients of the other only.
    private static string? Head(RouteLeg leg, bool six)
    {
        var sources = six ? leg.Sources6 : leg.Sources4;
        if (leg.IsBySource && sources.Count == 0)
        {
            return null;
        }

        return Inbound(leg.Rule)
            + (sources.Count > 0 ? $"{(six ? "ip6" : "ip")} saddr {{ {string.Join(", ", sources)} }} " : string.Empty);
    }

    // Returns the lines of the decision chain: the guards of the resolver, then the rules in order, a rule by name
    // followed by the lines that deal with the TCP connections it may take.
    private static IEnumerable<string> Decide(RoutePlan plan, IReadOnlyList<RouteLeg> live)
    {
        foreach (var line in Guard(plan))
        {
            yield return line;
        }

        var reading = Handing(plan);
        var handed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leg in live)
        {
            foreach (var line in Lines(leg, plan.Journal))
            {
                yield return line;
            }

            foreach (var line in Reading(leg, reading))
            {
                if (handed.Add(line))
                {
                    yield return line;
                }
            }
        }
    }

    // Returns what the ruleset hands over to the panel: nothing while no rule on the host matches by name.
    private static RouteReading? Handing(RoutePlan plan) => plan.HasNames && plan.Inbound.Count > 0 ? plan.Reading : null;

    // Writes the sets the reading goes by: the addresses looked at, the connections read and the ones that wait, and
    // the counter of what is handed over.
    private static void Handed(StringBuilder text, RoutePlan plan)
    {
        if (Handing(plan) is not { } reading)
        {
            return;
        }

        var seen = $"{NameDefaults.SeenMinutes.ToString(CultureInfo.InvariantCulture)}m";
        Kept(text, SeenSet(false), "ipv4_addr", "timeout", seen);
        Kept(text, SeenSet(true), "ipv6_addr", "timeout", seen);
        if (reading.Port is not null)
        {
            text.Append("\tcounter ").Append(TakenCounter).Append(" {\n\t\tpackets 0 bytes 0\n\t}\n\n");
            if (reading.Six)
            {
                text.Append("\tcounter ").Append(TakenCounter6).Append(" {\n\t\tpackets 0 bytes 0\n\t}\n\n");
            }
        }

        if (reading.Queue is not null)
        {
            var read = $"{NameDefaults.ReadSeconds.ToString(CultureInfo.InvariantCulture)}s";
            var wait = $"{NameDefaults.WaitSeconds.ToString(CultureInfo.InvariantCulture)}s";
            Kept(text, ReadSet(false), "ipv4_addr . inet_service . ipv4_addr", "dynamic,timeout", read);
            Kept(text, ReadSet(true), "ipv6_addr . inet_service . ipv6_addr", "dynamic,timeout", read);
            Kept(text, WaitSet(false), "ipv4_addr . inet_service . ipv4_addr", "dynamic,timeout", wait);
            Kept(text, WaitSet(true), "ipv6_addr . inet_service . ipv6_addr", "dynamic,timeout", wait);
        }
    }

    private static void Kept(StringBuilder text, string name, string type, string flags, string life)
    {
        text.Append("\tset ").Append(name).Append(" {\n");
        text.Append("\t\ttype ").Append(type).Append('\n');
        text.Append("\t\tflags ").Append(flags).Append('\n');
        text.Append("\t\ttimeout ").Append(life).Append('\n');
        text.Append("\t}\n\n");
    }

    // Writes the lines that take what the panel sent back and show it what the TCP connections noted say first: a
    // QUIC connection read is decided anew, a TCP connection read goes on, one a blocking rule takes is reset.
    private static void Back(StringBuilder text, RoutePlan plan)
    {
        if (Handing(plan)?.Queue is not { } queue)
        {
            return;
        }

        var read = Hex8(NameDefaults.Read);
        var reset = Hex8(NameDefaults.Reset);
        var number = queue.ToString(CultureInfo.InvariantCulture);
        foreach (var six in new[] { false, true })
        {
            var family = six ? "ip6" : "ip";
            var layer = six ? "ipv6" : "ipv4";
            text.Append($"\t\tmeta mark {read} meta nfproto {layer} meta l4proto udp update @{ReadSet(six)} ");
            text.Append($"{{ {family} saddr . udp sport . {family} daddr }}\n");
            text.Append($"\t\tmeta mark {{ {read}, {reset} }} meta nfproto {layer} meta l4proto tcp delete @{WaitSet(six)} ");
            text.Append($"{{ {family} saddr . tcp sport . {family} daddr }}\n");
        }

        text.Append($"\t\tmeta mark {reset} meta l4proto tcp reject with tcp reset\n");
        text.Append($"\t\tmeta mark {read} ct state new meta l4proto udp ct mark set ct mark and {Hex8(~RouteMarks)}\n");
        text.Append($"\t\tmeta mark {read} meta mark set 0x00000000\n");
        foreach (var six in new[] { false, true })
        {
            var family = six ? "ip6" : "ip";
            text.Append($"\t\t{family} saddr . tcp sport . {family} daddr @{WaitSet(six)} tcp flags & (fin | syn | rst) == 0x0 ");
            text.Append($"meta mark set ct mark and {Hex8(RouteMarks)} queue num {number} bypass\n");
        }
    }

    // Writes the lines that show the first packets of QUIC to the panel once the rules have decided them.
    private static void Asked(StringBuilder text, RoutePlan plan, IReadOnlyList<RouteLeg> live)
    {
        var reading = Handing(plan);
        foreach (var line in live.SelectMany(leg => Asking(leg, reading)).Distinct(StringComparer.Ordinal))
        {
            text.Append("\t\t").Append(line).Append('\n');
        }
    }

    // Returns the ports a rule matches by, written for one protocol.
    private static string Ports(RouteRule rule, string protocol) =>
        Pair(
            protocol,
            string.Join(", ", rule.SourcePorts.Select(port => port.Trim())),
            string.Join(", ", rule.Ports.Select(port => port.Trim())));

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

    // Writes the chain that sends the connections handed over in through a port the firewall of the host lets in.
    private static void Door(StringBuilder text, RoutePlan plan)
    {
        if (Handing(plan) is not { Port: not null, Door: { } door })
        {
            return;
        }

        text.Append("\n\tchain door {\n");
        text.Append("\t\ttype nat hook prerouting priority dstnat; policy accept;\n");
        text.Append("\t\tiifname != { ").Append(Quoted(plan.Inbound)).Append(" } accept\n");
        text.Append("\t\tmeta mark ").Append(Hex8(NameDefaults.Taken)).Append(" meta l4proto tcp redirect to :");
        text.Append(door.ToString(CultureInfo.InvariantCulture)).Append('\n');
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
