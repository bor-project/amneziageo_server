using System.Net;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class DnsGuardTests
{
    private static readonly byte[] Site = GeoBuilder.Site(("YOUTUBE", ["youtube.com"]));

    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "awgbor", Kind = OutboundKind.Wg, Mark = 0xA602, Table = 42602 },
    ];

    private static readonly DnsSettings On = DnsDefaults.Settings with { IsEnabled = true, Port = 5300 };

    private static readonly TunnelClient[] Clients = [new() { Id = 1, ConfigId = 1, Name = "office", Address = ["10.9.1.12/32"] }];

    [Fact]
    public void TheResolverTakesEveryQuestionOfTheClientsWhileItIsOn()
    {
        var text = RouteRuleset.Text(Plan(On, Rule(1, "77.88.8.0/24")));

        Assert.Contains("\tchain resolve {\n", text, StringComparison.Ordinal);
        Assert.Contains("\t\tmeta l4proto { tcp, udp } th dport 53 redirect to :5300\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuardsStandInATableOfTheirOwnThatKeepsItsSets()
    {
        var plan = Plan(On, Rule(1, "geosite:youtube"));
        var guard = RouteRuleset.Guarding(plan);

        Assert.DoesNotContain("th dport 853", RouteRuleset.Text(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("delete table inet amneziageo_dns", guard, StringComparison.Ordinal);
        Assert.Contains("flush chain inet amneziageo_dns guard\n", guard, StringComparison.Ordinal);
        Assert.Contains("\t\ttype filter hook forward priority filter - 4; policy accept;\n", guard, StringComparison.Ordinal);
        Assert.Contains("\tset free4 {\n\t\ttype ipv4_addr\n\t\tflags dynamic,timeout\n\t\ttimeout 1d\n\t}\n", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void AGuardTurnsTheClientAwayAtOnce()
    {
        var guard = RouteRuleset.Guarding(Plan(On, Rule(1, "geosite:youtube")));

        Assert.Contains("\t\tmeta nfproto ipv4 meta l4proto { tcp, udp } th dport 853 jump refuse\n", guard, StringComparison.Ordinal);
        Assert.Contains("\t\tip daddr @doh4 meta l4proto { tcp, udp } th dport 443 jump refuse\n", guard, StringComparison.Ordinal);
        Assert.Contains("\t\tip6 daddr @doh6 meta l4proto { tcp, udp } th dport 443 jump refuse\n", guard, StringComparison.Ordinal);
        Assert.Contains("\t\tmeta l4proto tcp reject with tcp reset\n\t\treject\n", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("drop", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void AClientThatNeverAsksTheResolverIsLetGoAfterAFewTries()
    {
        var guard = RouteRuleset.Guarding(Plan(On, Rule(1, "geosite:youtube")));

        Assert.Contains(
            "\t\tmeta nfproto ipv4 ct state new meta l4proto { tcp, udp } th dport 53 update @asked4 { ip saddr }\n",
            guard,
            StringComparison.Ordinal);
        Assert.Contains("\t\tip saddr @free4 accept\n\t\tip6 saddr @free6 accept\n", guard, StringComparison.Ordinal);
        Assert.Contains(
            "\t\tmeta nfproto ipv4 ip saddr != @asked4 meta l4proto { tcp, udp } th dport 853 "
            + "update @tried4 { ip saddr limit rate over 2/minute burst 3 packets } add @free4 { ip saddr } accept\n",
            guard,
            StringComparison.Ordinal);
        Assert.Contains(
            "\t\tip saddr != @asked4 ip daddr @doh4 meta l4proto { tcp, udp } th dport 443 "
            + "update @tried4 { ip saddr limit rate over 2/minute burst 3 packets } add @free4 { ip saddr } accept\n",
            guard,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuardsTakeOnlyTheClientsTheRulesByNameTake()
    {
        var named = Rule(1, "geosite:youtube") with { Inbounds = ["awg2"], Clients = ["office"] };
        var guard = RouteRuleset.Guarding(Plan(On, named, Rule(2, "77.88.8.0/24")));

        Assert.Contains(
            "\t\tiifname { \"awg2\" } ip saddr { 10.9.1.12/32 } meta nfproto ipv4 meta l4proto { tcp, udp } th dport 853 jump refuse\n",
            guard,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\t\tmeta nfproto ipv4 meta l4proto { tcp, udp } th dport 853 jump refuse\n", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("ip6 daddr @doh6 meta l4proto { tcp, udp } th dport 443 jump refuse", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void NoGuardStandsWhileTheNamesAreReadFromTheConnections()
    {
        var plan = Plan(On, Rule(1, "geosite:youtube")) with { Reading = new RouteReading(7, 7319) };

        Assert.DoesNotContain("th dport 853", RouteRuleset.Text(plan), StringComparison.Ordinal);
        Assert.Equal("table inet amneziageo_dns\ndelete table inet amneziageo_dns\n", RouteRuleset.Guarding(plan));
    }

    [Fact]
    public void AServerWithoutRulesByNameTakesTheGuardsAway()
    {
        var plan = Plan(On, Rule(1, "77.88.8.0/24"));

        Assert.Equal("table inet amneziageo_dns\ndelete table inet amneziageo_dns\n", RouteRuleset.Guarding(plan));
        Assert.DoesNotContain("doh4", RouteRuleset.Text(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void AClientOutsideTheRulesByNameIsNotGuarded()
    {
        var named = Rule(1, "geosite:youtube") with { Inbounds = ["awg2"] };
        var plan = Plan(On, named, Rule(2, "0.0.0.0/0"));

        var outside = Test(plan, new RouteQuery { Addresses = [IPAddress.Parse("8.8.8.8")], Port = 853, Inbound = "awg1" });
        var inside = Test(plan, new RouteQuery { Addresses = [IPAddress.Parse("8.8.8.8")], Port = 853, Inbound = "awg2" });

        Assert.Equal(string.Empty, outside.Guard);
        Assert.Equal(RouteProbe.Out, outside.Verdict);
        Assert.Equal("dot", inside.Guard);
    }

    [Fact]
    public void NoClientIsGuardedWhileTheNamesAreRead()
    {
        var plan = Plan(On, Rule(1, "geosite:youtube"), Rule(2, "0.0.0.0/0")) with { Reading = new RouteReading(7, 7319) };

        var verdict = Test(plan, new RouteQuery { Addresses = [IPAddress.Parse("1.1.1.1")], Port = 443 });

        Assert.Equal(string.Empty, verdict.Guard);
    }

    private static RouteVerdict Test(RoutePlan plan, RouteQuery query) => RouteProbe.Test(plan, query, (_, _) => false);

    private static RoutePlan Plan(DnsSettings dns, params RouteRule[] rules) =>
        RoutePlan.Build(rules, Ways, Index(), ["awg1", "awg2"], dns, clients: Clients);

    private static RouteRule Rule(long id, string target) => RouteDefaults.Fresh($"r{id}") with
    {
        Id = id,
        Position = (int)id,
        Outbound = "awgbor",
        Targets = [target],
    };

    private static GeoIndex Index()
    {
        var files = new MemoryGeoFiles();
        files.Put("site", Site);

        return GeoIndex.Load([new GeoSource { Name = "site", Kind = GeoKind.Site, Position = 1 }], files);
    }
}
