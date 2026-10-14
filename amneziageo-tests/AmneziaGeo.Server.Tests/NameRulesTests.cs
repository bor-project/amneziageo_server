using System.Net;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Names;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class NameRulesTests
{
    private const string Asked =
        "saddr . udp sport . ip daddr != @read4 udp dport 443 @th,64,8 & 0x80 == 0x80 queue num 7318 bypass";

    private const string Taken =
        "ip daddr != @seen4 tcp dport { 80, 443 } fib daddr type != local ip daddr != { 0.0.0.0/8, 10.0.0.0/8, "
        + "100.64.0.0/10, 127.0.0.0/8, 169.254.0.0/16, 172.16.0.0/12, 192.168.0.0/16, 224.0.0.0/3 } "
        + "limit rate 300/second burst 300 packets counter name \"taken\" tproxy ip to 127.0.0.1:7319 "
        + "meta mark set 0x0000a500 ct mark set 0x0000a500 accept";

    private const string Waited =
        "ip daddr != @seen4 tcp dport { 80, 443 } update @wait4 { ip saddr . tcp sport . ip daddr }";

    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "direct", Kind = OutboundKind.Local, Mark = 0xA601, Table = OutboundRules.MainTable },
        new() { Name = "far", Kind = OutboundKind.Wg, Mark = 0xA602, Table = 42602 },
    ];

    private static readonly RouteReading Both = new(NameDefaults.Queue, NameDefaults.Port);

    private static readonly RouteReading Shown = new(NameDefaults.Queue, null);

    private static readonly IPAddress Client = IPAddress.Parse("10.8.0.2");

    private static readonly IPAddress Site = IPAddress.Parse("203.0.113.7");

    [Fact]
    public void WhatARuleByNameMayTakeIsShownOrHandedOverToThePanel()
    {
        var text = RouteRuleset.Text(Plan(Both, Rule(1, "youtube.com")));
        var lines = text.Split('\n').Select(line => line.Trim()).ToArray();
        var own = Array.FindIndex(lines, line => line.StartsWith("ip daddr @n1v4 ", StringComparison.Ordinal));
        var asked = Array.IndexOf(lines, "ip " + Asked);

        Assert.Contains(Taken, lines);
        Assert.Contains(Waited, lines);
        Assert.Contains(lines, line => line.StartsWith("ip6 daddr != @seen6 tcp dport { 80, 443 } fib daddr type != local ip6 daddr != { ::/127, ", StringComparison.Ordinal)
            && line.Contains(" counter name \"taken6\" tproxy ip6 to [::1]:7319 ", StringComparison.Ordinal));
        Assert.Contains("ip6 daddr != @seen6 tcp dport { 80, 443 } update @wait6 { ip6 saddr . tcp sport . ip6 daddr }", lines);
        Assert.Contains("ip6 saddr . udp sport . ip6 daddr != @read6 udp dport 443 @th,64,8 & 0x80 == 0x80 queue num 7318 bypass", lines);
        Assert.True(own >= 0 && own < Array.IndexOf(lines, Taken));
        Assert.True(Array.IndexOf(lines, Taken) < Array.IndexOf(lines, Waited));
        Assert.True(asked > Array.IndexOf(lines, "jump decide") && asked < Array.IndexOf(lines, "chain decide {"));
        Assert.Contains("set seen4 {", lines);
        Assert.Contains("set read6 {", lines);
        Assert.Contains("set wait4 {", lines);
        Assert.Contains("counter taken {", lines);
        Assert.Contains("counter taken6 {", lines);
        Assert.Contains("timeout 5m", lines);
        Assert.Contains("timeout 30s", lines);
        Assert.Contains("timeout 10s", lines);
        Assert.Contains("flags dynamic,timeout", lines);
    }

    [Fact]
    public void WhatThePanelSentBackIsNotedLetGoOrReset()
    {
        var lines = RouteRuleset.Text(Plan(Both, Rule(1, "youtube.com"))).Split('\n').Select(line => line.Trim()).ToArray();
        var reset = Array.IndexOf(lines, "meta mark 0x00100000 meta l4proto tcp reject with tcp reset");
        var again = Array.IndexOf(lines, "meta mark 0x00080000 ct state new meta l4proto udp ct mark set ct mark and 0xffff0000");
        var shown = Array.IndexOf(
            lines,
            "ip saddr . tcp sport . ip daddr @wait4 tcp flags & (fin | syn | rst) == 0x0 meta mark set ct mark and 0x0000ffff queue num 7318 bypass");
        var kept = Array.IndexOf(lines, "ct mark and 0x0000ffff != 0x00000000 meta mark set ct mark and 0x0000ffff accept");

        Assert.Contains("meta mark 0x00080000 meta nfproto ipv4 meta l4proto udp update @read4 { ip saddr . udp sport . ip daddr }", lines);
        Assert.Contains("meta mark 0x00080000 meta nfproto ipv6 meta l4proto udp update @read6 { ip6 saddr . udp sport . ip6 daddr }", lines);
        Assert.Contains(
            "meta mark { 0x00080000, 0x00100000 } meta nfproto ipv4 meta l4proto tcp delete @wait4 { ip saddr . tcp sport . ip daddr }",
            lines);
        Assert.Contains("meta mark 0x00080000 meta mark set 0x00000000", lines);
        Assert.Contains(
            "ip6 saddr . tcp sport . ip6 daddr @wait6 tcp flags & (fin | syn | rst) == 0x0 meta mark set ct mark and 0x0000ffff queue num 7318 bypass",
            lines);
        Assert.True(reset > Array.IndexOf(lines, "ct state invalid drop"));
        Assert.True(reset < again && again < shown && shown < kept);
    }

    [Fact]
    public void NothingIsShownWhileNoRuleGoesByNameOrThePanelTakesNothing()
    {
        var ranges = RouteRuleset.Text(Plan(Both, Rule(1, "77.88.8.0/24")));
        var quiet = RouteRuleset.Text(Plan(null, Rule(1, "youtube.com")));
        var off = RouteRuleset.Text(Plan(Both, Rule(1, "youtube.com") with { IsEnabled = false }));

        foreach (var text in new[] { ranges, quiet, off })
        {
            Assert.DoesNotContain("queue", text, StringComparison.Ordinal);
            Assert.DoesNotContain("tproxy", text, StringComparison.Ordinal);
            Assert.DoesNotContain("reject", text, StringComparison.Ordinal);
            Assert.DoesNotContain("seen4", text, StringComparison.Ordinal);
            Assert.DoesNotContain("read4", text, StringComparison.Ordinal);
            Assert.DoesNotContain("wait4", text, StringComparison.Ordinal);
            Assert.DoesNotContain("counter", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OnlyWhatThePanelTakesIsHandedOver()
    {
        var queue = RouteRuleset.Text(Plan(Shown, Rule(1, "youtube.com")));
        var port = RouteRuleset.Text(Plan(new RouteReading(null, NameDefaults.Port), Rule(1, "youtube.com")));
        var four = RouteRuleset.Text(Plan(new RouteReading(NameDefaults.Queue, NameDefaults.Port, false), Rule(1, "youtube.com")));

        Assert.Contains("queue num 7318", queue, StringComparison.Ordinal);
        Assert.Contains(Waited, queue, StringComparison.Ordinal);
        Assert.Contains("reject with tcp reset", queue, StringComparison.Ordinal);
        Assert.DoesNotContain("tproxy", queue, StringComparison.Ordinal);
        Assert.DoesNotContain("counter", queue, StringComparison.Ordinal);
        Assert.Contains("tproxy ip to", port, StringComparison.Ordinal);
        Assert.Contains("tproxy ip6 to", port, StringComparison.Ordinal);
        Assert.Contains("set seen4 {", port, StringComparison.Ordinal);
        Assert.DoesNotContain("queue", port, StringComparison.Ordinal);
        Assert.DoesNotContain("reject", port, StringComparison.Ordinal);
        Assert.DoesNotContain("read4", port, StringComparison.Ordinal);
        Assert.DoesNotContain("wait4", port, StringComparison.Ordinal);
        Assert.Contains("tproxy ip to", four, StringComparison.Ordinal);
        Assert.DoesNotContain("tproxy ip6 to", four, StringComparison.Ordinal);
        Assert.DoesNotContain("taken6", four, StringComparison.Ordinal);
        Assert.Contains("update @wait6 {", four, StringComparison.Ordinal);
    }

    [Fact]
    public void WhereTheHostDropsWhatComesInForAnotherAddressTheConnectionsComeInThroughAPort()
    {
        var sent = new RouteReading(NameDefaults.Queue, NameDefaults.Port, true, 8443);
        var lines = RouteRuleset.Text(Plan(sent, Rule(1, "youtube.com"))).Split('\n').Select(line => line.Trim()).ToArray();
        var door = Array.IndexOf(lines, "chain door {");
        var plain = RouteRuleset.Text(Plan(Both, Rule(1, "youtube.com")));
        var shown = RouteRuleset.Text(Plan(sent with { Port = null }, Rule(1, "youtube.com")));
        var ranges = RouteRuleset.Text(Plan(sent, Rule(1, "77.88.8.0/24")));

        Assert.True(door > Array.IndexOf(lines, "chain decide {"));
        Assert.Equal("type nat hook prerouting priority dstnat; policy accept;", lines[door + 1]);
        Assert.Equal("iifname != { \"awg1\" } accept", lines[door + 2]);
        Assert.Equal("meta mark 0x0000a500 meta l4proto tcp redirect to :8443", lines[door + 3]);
        Assert.Contains(Taken, lines);
        foreach (var text in new[] { plain, shown, ranges })
        {
            Assert.DoesNotContain("chain door", text, StringComparison.Ordinal);
            Assert.DoesNotContain("redirect to :8443", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheConditionsOfARuleNarrowWhatIsShown()
    {
        var tcp = Rule(1, "youtube.com") with { Protocol = RouteProtocol.Tcp, Ports = ["443", "8443"], Inbounds = ["awg1"] };
        var udp = Rule(2, "vk.com") with { Protocol = RouteProtocol.Udp, Sources = ["10.8.0.0/25"] };
        var plan = Plan(Both, tcp, udp);
        var first = RouteRuleset.Reading(plan.Legs[0], Both);

        Assert.Equal(4, first.Count);
        Assert.All(first, line => Assert.Contains(" tcp dport { 80, 443 } tcp dport { 443, 8443 } ", line, StringComparison.Ordinal));
        Assert.StartsWith("iifname { \"awg1\" } ip daddr != @seen4 tcp dport { 80, 443 } tcp dport { 443, 8443 } fib daddr ", first[0], StringComparison.Ordinal);
        Assert.Equal(
            "iifname { \"awg1\" } ip daddr != @seen4 tcp dport { 80, 443 } tcp dport { 443, 8443 } update @wait4 { ip saddr . tcp sport . ip daddr }",
            first[1]);
        Assert.Empty(RouteRuleset.Asking(plan.Legs[0], Both));
        Assert.Empty(RouteRuleset.Reading(plan.Legs[1], Both));
        Assert.Equal(["ip saddr { 10.8.0.0/25 } ip " + Asked], RouteRuleset.Asking(plan.Legs[1], Both));
    }

    [Fact]
    public void TwoRulesThatShowTheSameDoItOnce()
    {
        var text = RouteRuleset.Text(Plan(Both, Rule(1, "youtube.com"), Rule(2, "vk.com")));

        Assert.Single(text.Split('\n'), line => line.Trim() == Taken);
        Assert.Single(text.Split('\n'), line => line.Trim() == Waited);
        Assert.Single(text.Split('\n'), line => line.Trim() == "ip " + Asked);
    }

    [Fact]
    public async Task ANameReadOutOfAConnectionBringsItsAddressIntoTheSetOfItsRule()
    {
        var host = new Ledger();
        var desk = new NameDesk(new DnsSets(host), host);
        var plan = Plan(Both, Rule(1, "youtube.com"));

        var told = desk.Learn(plan, "www.youtube.com", Site);
        var landed = await desk.LandAsync(plan, CancellationToken.None);
        var again = desk.Learn(plan, "www.youtube.com", Site);
        var other = desk.Learn(plan, "example.org", Site);

        Assert.True(told);
        Assert.Equal(1, landed);
        Assert.Contains("add element inet amneziageo_rt n1v4 { 203.0.113.7 timeout 3600s }", host.Ruleset, StringComparison.Ordinal);
        Assert.False(again);
        Assert.False(other);
        Assert.Equal(1, desk.Learned);
    }

    [Fact]
    public void TheNameOfAConnectionDecidesWhereItGoes()
    {
        var host = new Ledger();
        var desk = new NameDesk(new DnsSets(host), host);
        var plan = Plan(Both, Rule(1, "youtube.com"), Rule(2, "ads.example") with { Action = RouteAction.Block });

        var named = desk.Decide(plan, Query("www.youtube.com"));
        var blocked = desk.Decide(plan, Query("x.ads.example"));
        var plain = desk.Decide(plan, Query("example.org"));
        var bare = desk.Decide(plan, Query(string.Empty));

        Assert.Equal((RouteProbe.Out, 1L, 0xA602u, true), (named.Verdict, named.Leg?.Rule.Id, named.Mark, named.Goes));
        Assert.Equal((RouteProbe.Block, 2L, 0u, false), (blocked.Verdict, blocked.Leg?.Rule.Id, blocked.Mark, blocked.Goes));
        Assert.Equal((RouteProbe.Host, null, 0u, true), (plain.Verdict, plain.Leg?.Rule.Id, plain.Mark, plain.Goes));
        Assert.Equal(RouteProbe.Host, bare.Verdict);
    }

    [Fact]
    public void AConnectionOnAnotherWayThanTheRulesNameGoesOnAndOnlyABlockedOneIsCut()
    {
        var host = new Ledger();
        var desk = new NameDesk(new DnsSets(host), host);
        var plan = Plan(Both, Rule(1, "youtube.com"), Rule(2, "ads.example") with { Action = RouteAction.Block });
        var named = desk.Decide(plan, Query("www.youtube.com"));
        var blocked = desk.Decide(plan, Query("x.ads.example"));
        var plain = desk.Decide(plan, Query("example.org"));
        var spread = named with { Leg = named.Leg! with { Exit = new RouteExit(BalanceStrategy.Round, [0xA601, 0xA602]) } };

        Assert.True(named.Keeps(0xA602));
        Assert.False(named.Keeps(0));
        Assert.False(named.Keeps(0xA601));
        Assert.True(spread.Keeps(0xA601));
        Assert.True(spread.Keeps(0xA602));
        Assert.False(spread.Keeps(0xA603));
        Assert.True(plain.Keeps(0));
        Assert.False(plain.Keeps(0xA602));
        Assert.False(blocked.Keeps(0));
        Assert.False(blocked.Keeps(0xA602));
        Assert.True(named.Misses(0));
        Assert.True(named.Misses(0xA601));
        Assert.False(named.Misses(0xA602));
        Assert.True(plain.Misses(0xA602));
        Assert.False(plain.Misses(0));
        Assert.False(blocked.Misses(0));
        Assert.False(blocked.Misses(0xA602));
    }

    [Fact]
    public async Task TheAddressesLookedAtGoToTheHostTogether()
    {
        var host = new Ledger();
        var desk = new NameDesk(new DnsSets(host), host);

        desk.Seen(Site);
        desk.Seen(Site);
        desk.Seen(IPAddress.Parse("2001:db8::7"));
        var sent = await desk.NoteAsync(CancellationToken.None);
        var none = await desk.NoteAsync(CancellationToken.None);

        Assert.Equal(2, sent);
        Assert.Equal(0, none);
        Assert.Equal(
            "add element inet amneziageo_rt seen4 { 203.0.113.7 }\nadd element inet amneziageo_rt seen6 { 2001:db8::7 }\n",
            host.Ruleset);
    }

    [Fact]
    public async Task AnAddressLookedAtWakesWhoeverSendsThemToTheHost()
    {
        var host = new Ledger();
        var desk = new NameDesk(new DnsSets(host), host);

        var asleep = desk.SeenAsync(CancellationToken.None).AsTask();
        await Task.Delay(100);
        var slept = !asleep.IsCompleted;
        desk.Seen(Site);
        var woken = await asleep.WaitAsync(TimeSpan.FromSeconds(5));
        await desk.NoteAsync(CancellationToken.None);
        var again = desk.SeenAsync(CancellationToken.None).AsTask();
        await Task.Delay(100);

        Assert.True(slept);
        Assert.True(woken);
        Assert.False(again.IsCompleted);
    }

    [Fact]
    public async Task AnAddressLookedAtGoesToTheHostAtOnceWhenAsked()
    {
        var host = new Ledger();
        var desk = new NameDesk(new DnsSets(host), host);

        await desk.NoteAsync(IPAddress.Parse("2001:db8::7"), CancellationToken.None);

        Assert.Equal("add element inet amneziageo_rt seen6 { 2001:db8::7 }\n", host.Ruleset);
    }

    [Fact]
    public void AConnectionThePanelCarriesLeavesWithTheMarkTheFirewallWouldGiveIt()
    {
        var marks = new NameMarks();
        var one = RouteExit.One(0xA602);
        var round = new RouteExit(BalanceStrategy.Round, [0xA601, 0xA602]);
        var sticky = new RouteExit(BalanceStrategy.Sticky, [0xA601, 0xA602, 0xA603]) { Seed = 7 };
        var clients = Enumerable.Range(2, 60).Select(last => IPAddress.Parse($"10.8.0.{last}")).ToArray();

        Assert.Equal(0xA602u, marks.Pick(one, Client));
        Assert.Equal(2, Enumerable.Range(0, 6).Select(_ => marks.Pick(round, Client)).Distinct().Count());
        Assert.Single(Enumerable.Range(0, 6).Select(_ => marks.Pick(sticky, Client)).Distinct());
        Assert.Equal(3, clients.Select(client => marks.Pick(sticky, client)).Distinct().Count());
        Assert.NotEqual(NameMarks.Hash(Client.GetAddressBytes(), 7), NameMarks.Hash(Client.GetAddressBytes(), 8));
    }

    [Theory]
    [InlineData(7u, 2, "10.8.0.7", 0)]
    [InlineData(7u, 2, "10.8.0.200", 0)]
    [InlineData(7u, 2, "10.8.0.2", 1)]
    [InlineData(7u, 2, "10.8.0.44", 1)]
    [InlineData(7u, 2, "fd00:8::2", 0)]
    [InlineData(7u, 2, "fd00:8::a7", 1)]
    [InlineData(7u, 3, "10.8.0.253", 0)]
    [InlineData(7u, 3, "10.8.0.3", 1)]
    [InlineData(7u, 3, "10.8.0.200", 1)]
    [InlineData(7u, 3, "10.8.0.44", 2)]
    [InlineData(7u, 3, "fd00:8::1:44", 1)]
    [InlineData(2654435769u, 2, "10.8.0.2", 0)]
    [InlineData(2654435769u, 2, "10.8.0.7", 1)]
    [InlineData(2654435769u, 2, "fd00:8::a7", 0)]
    [InlineData(2654435769u, 2, "fd00:8::3", 1)]
    [InlineData(2654435769u, 3, "10.8.0.253", 0)]
    [InlineData(2654435769u, 3, "10.8.0.3", 1)]
    [InlineData(2654435769u, 3, "10.8.0.44", 2)]
    [InlineData(2654435769u, 3, "fd00:8::1:44", 0)]
    [InlineData(2654435769u, 3, "fd00:8::2", 2)]
    public void TheChoiceForAClientIsTheOneTheKernelMakes(uint seed, int count, string client, int choice)
    {
        var marks = Enumerable.Range(0, count).Select(at => 0xA601u + (uint)at).ToArray();
        var exit = new RouteExit(BalanceStrategy.Sticky, marks) { Seed = seed };

        Assert.Equal(0xA601u + (uint)choice, new NameMarks().Pick(exit, IPAddress.Parse(client)));
    }

    [Theory]
    [InlineData("93.184.216.34", false)]
    [InlineData("203.0.113.7", false)]
    [InlineData("2001:db8::7", false)]
    [InlineData("10.8.0.9", true)]
    [InlineData("100.64.1.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("172.31.255.1", true)]
    [InlineData("192.168.250.2", true)]
    [InlineData("224.0.0.251", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:10.8.0.9", true)]
    [InlineData("fd42::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("ff02::fb", true)]
    public void ThePanelCarriesNoConnectionToAnAddressOfTheHostAPrivateOrAGroupOne(string address, bool closed)
    {
        Assert.Equal(closed, NameReach.IsClosed(IPAddress.Parse(address)));
    }

    [Fact]
    public void ThePanelCarriesNoConnectionPastWhatTheEndpointsKeepTheirClientsFrom()
    {
        var first = ConfigDefaults.Fresh("awg1") with { Address = ["198.51.100.1/24", "fd42:1::1/112"], Blocked = ["203.0.113.0/24", "2001:db8:9::/48"] };
        var second = ConfigDefaults.Fresh("awg2") with { Address = ["192.0.2.1/24"], Blocked = [] };
        var reach = NameReach.Of([first, second]);
        var own = IPAddress.Parse("198.51.100.2");
        var other = IPAddress.Parse("192.0.2.7");
        var stranger = IPAddress.Parse("198.18.0.9");

        Assert.Equal("awg1", reach.Inbound(own));
        Assert.Equal("awg1", reach.Inbound(IPAddress.Parse("fd42:1::7")));
        Assert.Equal("awg2", reach.Inbound(other));
        Assert.Equal(string.Empty, reach.Inbound(stranger));
        Assert.True(reach.Lets(own, IPAddress.Parse("93.184.216.34")));
        Assert.True(reach.Lets(own, IPAddress.Parse("2001:db8::7")));
        Assert.False(reach.Lets(own, other));
        Assert.False(reach.Lets(other, own));
        Assert.False(reach.Lets(own, IPAddress.Parse("192.168.250.2")));
        Assert.False(reach.Lets(own, IPAddress.Parse("203.0.113.7")));
        Assert.False(reach.Lets(own, IPAddress.Parse("2001:db8:9::7")));
        Assert.True(reach.Lets(other, IPAddress.Parse("203.0.113.7")));
        Assert.False(reach.Lets(stranger, IPAddress.Parse("203.0.113.7")));
        Assert.True(reach.Lets(stranger, IPAddress.Parse("93.184.216.34")));
        Assert.True(NameReach.None.Lets(own, IPAddress.Parse("93.184.216.34")));
    }

    [Fact]
    public async Task AHostThatDropsWhatComesInForAnotherAddressIsTold()
    {
        var guarded = new Tools();
        guarded.Answers["nft list chain ip filter ufw-not-local"] = new CommandResult(
            0,
            "table ip filter {\n\tchain ufw-not-local {\n\t\tfib daddr type local counter packets 9 bytes 540 return\n\t\tcounter packets 4 bytes 240 drop\n\t}\n}\n",
            string.Empty);
        var bare = new Tools();
        bare.Answers["nft list chain ip filter ufw-not-local"] = new CommandResult(1, string.Empty, "Error: No such file or directory");
        var empty = new Tools();
        empty.Answers["nft list chain ip filter ufw-not-local"] = new CommandResult(0, "table ip filter {\n\tchain ufw-not-local {\n\t}\n}\n", string.Empty);

        Assert.True(await new FirewallHost(guarded, new Ledger(), string.Empty).DropsForeignAsync(CancellationToken.None));
        Assert.False(await new FirewallHost(bare, new Ledger(), string.Empty).DropsForeignAsync(CancellationToken.None));
        Assert.False(await new FirewallHost(empty, new Ledger(), string.Empty).DropsForeignAsync(CancellationToken.None));
    }

    [Fact]
    public void TheRecordOfAConnectionThePanelCarriedTellsHowItWent()
    {
        var plan = Plan(Both, Rule(1, "youtube.com"));
        var at = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        var record = AccessReader.Carried(
            at,
            new IPEndPoint(Client, 40000),
            new IPEndPoint(Site, 443),
            "awg1",
            "www.youtube.com",
            RouteProbe.Out,
            plan.Legs[0],
            plan,
            0xA602);

        Assert.Equal((at, "10.8.0.2", 40000, "awg1", AccessFlow.Tcp), (record.At, record.Source, record.SourcePort, record.Inbound, record.Protocol));
        Assert.Equal(("203.0.113.7", 443, "www.youtube.com"), (record.Target, record.Port, record.Name));
        Assert.Equal((AccessVerdict.Out, 1L, "r1", "far", AccessPath.Relay), (record.Verdict, record.Rule, record.RuleName, record.Way, record.Path));
    }

    [Fact]
    public void ANameTheConnectionToldIsPutOnTheRecordsOfTheClient()
    {
        var names = new AccessNames { IsOn = true };

        names.Learn(Client, Site, "www.youtube.com");

        Assert.Equal("www.youtube.com", names.Name(Client, Site));
        Assert.Equal("www.youtube.com", names.Name(IPAddress.Parse("10.8.0.9"), Site));
        Assert.Equal(string.Empty, names.Name(Client, IPAddress.Parse("203.0.113.8")));
    }

    private static RouteQuery Query(string name) => new()
    {
        Name = name,
        Addresses = [Site],
        Port = 443,
        Protocol = RouteProtocol.Tcp,
        Sources = [Client],
        Inbound = "awg1",
    };

    private static RouteRule Rule(long id, string target) => RouteDefaults.Fresh($"r{id}") with
    {
        Id = id,
        Position = (int)id,
        Action = RouteAction.Out,
        Outbound = "far",
        Targets = [target],
    };

    private static RoutePlan Plan(RouteReading? reading, params RouteRule[] rules) =>
        RoutePlan.Build(
            rules,
            Ways,
            GeoIndex.Load([], new MemoryGeoFiles()),
            ["awg1"],
            DnsDefaults.Settings with { IsEnabled = true }) with { Reading = reading };
}
