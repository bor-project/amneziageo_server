using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class RouteConditionsTests
{
    private static readonly byte[] Ip = GeoBuilder.Ip(("RU", ["77.88.8.0/24"]));

    private static readonly byte[] Site = GeoBuilder.Site(("YOUTUBE", ["youtube.com"]));

    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "awgbor", Kind = OutboundKind.Wg, Mark = 0xA602, Table = 42602 },
    ];

    private static readonly TunnelClient[] Clients =
    [
        new() { Id = 1, ConfigId = 1, Name = "bor", Address = ["10.9.1.12/32"] },
        new() { Id = 2, ConfigId = 1, Name = "bor-phone", Address = ["10.9.1.13/32", "fd00::13/128"] },
        new() { Id = 3, ConfigId = 2, Name = "guest", Address = ["10.9.2.5/32"] },
    ];

    [Fact]
    public void AnInterfaceNarrowsTheRuleToTheTrafficComingInOnIt()
    {
        var rule = Out() with { Inbounds = ["awg1"] };

        Assert.Equal(["iifname { \"awg1\" } meta mark set 0xa602 return"], Lines(rule));
    }

    [Fact]
    public void AnInterfaceGoesAheadOfTheSourcesAndTheTargets()
    {
        var rule = Out() with { Inbounds = ["awg1", "awg2"], Sources = ["10.8.0.0/24"], Targets = ["1.2.3.0/24"] };

        Assert.Equal(
            ["iifname { \"awg1\", \"awg2\" } ip saddr { 10.8.0.0/24 } ip daddr @r1v4 meta mark set 0xa602 return"],
            Lines(rule));
    }

    [Theory]
    [InlineData(RouteProtocol.Any, "meta l4proto { tcp, udp } th sport { 5000-6000 } th dport { 443 } meta mark set 0xa602 return")]
    [InlineData(RouteProtocol.Udp, "udp sport { 5000-6000 } udp dport { 443 } meta mark set 0xa602 return")]
    public void ASourcePortStandsBeforeTheDestinationPort(string protocol, string line)
    {
        var rule = Out() with { Protocol = protocol, SourcePorts = ["5000-6000"], Ports = ["443"] };

        Assert.Equal([line], Lines(rule));
    }

    [Fact]
    public void ASourcePortAloneNeedsNoDestinationPort()
    {
        var rule = Out() with { Protocol = RouteProtocol.Tcp, SourcePorts = ["22"] };

        Assert.Equal(["tcp sport { 22 } meta mark set 0xa602 return"], Lines(rule));
    }

    [Fact]
    public void AClientIsMatchedByItsOwnAddresses()
    {
        var leg = Leg(Out() with { Clients = ["BOR"] });
        var phone = Leg(Out() with { Clients = ["bor-phone"] });

        Assert.True(leg.IsLive);
        Assert.Equal(["10.9.1.12/32"], leg.Sources4);
        Assert.Empty(leg.Sources6);
        Assert.Equal(["ip saddr { 10.9.1.12/32 } meta mark set 0xa602 return"], RouteRuleset.Lines(leg));
        Assert.Equal(
            [
                "ip saddr { 10.9.1.13/32 } meta mark set 0xa602 return",
                "ip6 saddr { fd00::13/128 } meta mark set 0xa602 return",
            ],
            RouteRuleset.Lines(phone));
    }

    [Fact]
    public void AClientJoinsTheAddressesTheRuleNamesByHand()
    {
        var leg = Leg(Out() with { Clients = ["guest"], Sources = ["10.8.0.0/24"] });

        Assert.Equal(["10.8.0.0/24", "10.9.2.5/32"], leg.Sources4);
        Assert.Empty(leg.Sources6);
        Assert.Equal(["ip saddr { 10.8.0.0/24, 10.9.2.5/32 } meta mark set 0xa602 return"], RouteRuleset.Lines(leg));
    }

    [Fact]
    public void AClientThePanelLostIsPassedOverWhileAnotherStays()
    {
        var leg = Leg(Out() with { Clients = ["gone", "guest"] });

        Assert.True(leg.IsLive);
        Assert.Equal(["10.9.2.5/32"], leg.Sources4);
    }

    [Fact]
    public void ARuleNamingNoClientThePanelHoldsStaysOff()
    {
        var leg = Leg(Out() with { Clients = ["gone"] });

        Assert.Equal("unknown-client", leg.Fault?.Code);
        Assert.False(leg.IsOnHost);
    }

    [Fact]
    public void ARuleNamingNoInterfaceThePanelHoldsStaysOff()
    {
        var leg = Leg(Out() with { Inbounds = ["awg9"] });

        Assert.Equal("unknown-inbound", leg.Fault?.Code);
        Assert.False(leg.IsOnHost);
    }

    [Theory]
    [InlineData("bad name", "", "", "bad-rule-client")]
    [InlineData("", "Awg1", "", "bad-rule-inbound")]
    [InlineData("", "", "0", "bad-source-port")]
    public void ABrokenConditionIsRefusedByCode(string client, string inbound, string port, string code)
    {
        var rule = Out() with
        {
            Clients = client.Length > 0 ? [client] : [],
            Inbounds = inbound.Length > 0 ? [inbound] : [],
            SourcePorts = port.Length > 0 ? [port] : [],
        };

        Assert.Equal(code, RouteRules.Check(rule)?.Code);
    }

    [Fact]
    public void ADirectRuleLeavesTheWayTheHostDoesWithoutAMark()
    {
        var rule = RouteDefaults.Fresh("home") with { Id = 4, Action = RouteAction.Direct, Targets = ["77.88.8.0/24"] };
        var leg = Leg(rule);

        Assert.True(leg.IsLive);
        Assert.True(leg.IsDirect);
        Assert.Equal(0u, leg.Mark);
        Assert.Equal(["ip daddr @r4v4 return"], RouteRuleset.Lines(leg));
    }

    [Fact]
    public void TheBasicListsAreReadAheadOfTheRulesBlockFirst()
    {
        var basic = new RouteBasic { Direct = ["77.88.8.0/24"], Block = ["geosite:youtube"] };

        var plan = RoutePlan.Build([Out() with { Targets = ["1.2.3.0/24"] }], Ways, Index(), ["awg1"], clients: Clients, basic: basic);

        Assert.Equal([RouteBasic.BlockId, RouteBasic.DirectId, 1], plan.Legs.Select(leg => leg.Rule.Id));
        Assert.Equal(["ip daddr @nb1v4 drop", "ip6 daddr @nb1v6 drop"], RouteRuleset.Lines(plan.Legs[0]));
        Assert.Equal(["ip daddr @rb2v4 return"], RouteRuleset.Lines(plan.Legs[1]));
    }

    [Fact]
    public void AnEmptyBasicListTakesNoLineAtAll()
    {
        var plan = RoutePlan.Build([Out()], Ways, Index(), ["awg1"], basic: new RouteBasic { Direct = ["geoip:ru"] });

        Assert.Equal([RouteBasic.DirectId, 1], plan.Legs.Select(leg => leg.Rule.Id));
    }

    [Fact]
    public void TheSetsOfTheBasicListsAreGivenBackToTheirNumbers()
    {
        Assert.Equal(RouteBasic.BlockId, RouteRuleset.NameSetRule(RouteRuleset.NameSet(RouteBasic.BlockId, false)));
        Assert.Equal(RouteBasic.DirectId, RouteRuleset.NameSetRule(RouteRuleset.NameSet(RouteBasic.DirectId, true)));
        Assert.Equal(7L, RouteRuleset.NameSetRule("n7v4"));
        Assert.Null(RouteRuleset.NameSetRule("doh4"));
        Assert.Null(RouteRuleset.NameSetRule("r7v4"));
        Assert.Null(RouteRuleset.NameSetRule("nbv4"));
    }

    [Fact]
    public void ABasicListTakesMoreTargetsThanARule()
    {
        var many = Enumerable.Range(1, 200).Select(at => $"10.{at}.0.0/16").ToArray();

        Assert.Null(RouteRules.CheckBasic(new RouteBasic { Block = many }));
        Assert.Equal("too-many", RouteRules.Check(Out() with { Targets = many })?.Code);
        Assert.Equal("bad-target", RouteRules.CheckBasic(new RouteBasic { Direct = ["what is this"] })?.Code);
    }

    private static RouteRule Out() =>
        RouteDefaults.Fresh("rule") with { Id = 1, Action = RouteAction.Out, Outbound = "awgbor" };

    private static RouteLeg Leg(RouteRule rule) =>
        Assert.Single(RoutePlan.Build([rule], Ways, Index(), ["awg1", "awg2"], clients: Clients).Legs);

    private static IReadOnlyList<string> Lines(RouteRule rule) => RouteRuleset.Lines(Leg(rule));

    private static GeoIndex Index()
    {
        var files = new MemoryGeoFiles();
        files.Put("ip", Ip);
        files.Put("site", Site);

        return GeoIndex.Load(
            [
                new GeoSource { Name = "ip", Kind = GeoKind.Ip, Position = 1 },
                new GeoSource { Name = "site", Kind = GeoKind.Site, Position = 2 },
            ],
            files);
    }
}
