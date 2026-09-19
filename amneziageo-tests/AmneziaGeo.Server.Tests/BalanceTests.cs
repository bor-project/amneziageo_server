using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class BalanceTests
{
    private static readonly byte[] Ip = GeoBuilder.Ip(("RU", ["77.88.8.0/24", "2a02:6b8::/32"]));

    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "direct", Kind = OutboundKind.Local, Mark = 0xA601, Table = OutboundRules.MainTable },
        new() { Name = "awgbor", Kind = OutboundKind.Wg, Mark = 0xA602, Table = 42602 },
        new() { Name = "awgoff", Kind = OutboundKind.Wg, Mark = 0xA603, Table = 42603, IsEnabled = false },
    ];

    [Fact]
    public void ABalancerWithoutAStrategyTheServerKnowsIsRefused()
    {
        var fault = BalanceRules.Check(Group(BalanceStrategy.Priority) with { Strategy = "sideways" });

        Assert.Equal("bad-strategy", fault?.Code);
    }

    [Fact]
    public void ABalancerThatPicksFromNothingIsRefused()
    {
        var fault = BalanceRules.Check(BalanceDefaults.Fresh("empty"));

        Assert.Equal("no-members", fault?.Code);
    }

    [Fact]
    public void ABalancerThatNamesOneOutboundTwiceIsRefused()
    {
        var fault = BalanceRules.Check(Group(BalanceStrategy.Round) with { Members = ["direct", "direct"] });

        Assert.Equal("same-member", fault?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a name that is very much longer than the panel takes from anyone")]
    public void ABalancerUnderANameThatDoesNotHoldIsRefused(string name)
    {
        var fault = BalanceRules.Check(Group(BalanceStrategy.Priority) with { Name = name });

        Assert.Equal("bad-balancer-name", fault?.Code);
    }

    [Fact]
    public void PriorityTakesTheFirstOutboundThatCarriesTraffic()
    {
        var leg = Leg(Group(BalanceStrategy.Priority), ["direct"]);

        Assert.True(leg.IsLive);
        Assert.Equal(0xA601u, leg.Mark);
        Assert.False(leg.Exit.IsSpread);
    }

    [Fact]
    public void PriorityTakesTheOneStandingFirstWhenBothCarryTraffic()
    {
        var leg = Leg(Group(BalanceStrategy.Priority), ["direct", "awgbor"]);

        Assert.Equal(0xA602u, leg.Mark);
    }

    [Fact]
    public void ARoundCarriesTheMarksOfEveryLiveOutbound()
    {
        var leg = Leg(Group(BalanceStrategy.Round), ["direct", "awgbor"]);

        Assert.True(leg.Exit.IsSpread);
        Assert.Equal([0xA602u, 0xA601u], leg.Exit.Marks);
        Assert.Contains(
            "ip daddr @r1v4 meta mark set numgen inc mod 2 map { 0 : 0xa602, 1 : 0xa601 } return",
            RouteRuleset.Lines(leg));
    }

    [Fact]
    public void StickinessPicksTheOutboundByTheAddressOfTheClient()
    {
        var lines = RouteRuleset.Lines(Leg(Group(BalanceStrategy.Sticky), ["direct", "awgbor"]));

        Assert.Contains(
            "ip daddr @r1v4 meta mark set jhash ip saddr mod 2 seed 0x9e3779b1 map { 0 : 0xa602, 1 : 0xa601 } return",
            lines);
        Assert.Contains(
            "ip6 daddr @r1v6 meta mark set jhash ip6 saddr mod 2 seed 0x9e3779b1 map { 0 : 0xa602, 1 : 0xa601 } return",
            lines);
    }

    [Fact]
    public void TheSeedOfAStickyBalancerStaysTheSameAcrossWrites()
    {
        var group = Group(BalanceStrategy.Sticky);

        Assert.Equal(RouteRuleset.Lines(Leg(group, ["direct", "awgbor"])), RouteRuleset.Lines(Leg(group, ["direct", "awgbor"])));
        Assert.NotEqual(group.Seed, (group with { Id = 2 }).Seed);
    }

    [Fact]
    public void ARuleThatMatchesEverythingStillPicksByTheAddressOfTheClient()
    {
        var rule = RouteDefaults.Fresh("all") with { Id = 1, Outbound = "pick" };
        var leg = Assert.Single(Plan([Group(BalanceStrategy.Sticky)], ["direct", "awgbor"], rule).Legs);

        Assert.Equal(
            [
                "meta nfproto ipv4 meta mark set jhash ip saddr mod 2 seed 0x9e3779b1 map { 0 : 0xa602, 1 : 0xa601 } return",
                "meta nfproto ipv6 meta mark set jhash ip6 saddr mod 2 seed 0x9e3779b1 map { 0 : 0xa602, 1 : 0xa601 } return",
            ],
            RouteRuleset.Lines(leg));
    }

    [Fact]
    public void ARuleThatMatchesEverythingTakesOneLineUnderARound()
    {
        var rule = RouteDefaults.Fresh("all") with { Id = 1, Outbound = "pick" };
        var leg = Assert.Single(Plan([Group(BalanceStrategy.Round)], ["direct", "awgbor"], rule).Legs);

        Assert.Equal(
            ["meta mark set numgen inc mod 2 map { 0 : 0xa602, 1 : 0xa601 } return"],
            RouteRuleset.Lines(leg));
    }

    [Fact]
    public void ABalancerNothingAnswersInKeepsTheRuleOffTheHost()
    {
        var leg = Leg(Group(BalanceStrategy.Round), []);

        Assert.False(leg.IsLive);
        Assert.Equal("no-live-member", leg.Fault?.Code);
    }

    [Fact]
    public void ABalancerThatIsOffKeepsTheRuleOffTheHost()
    {
        var leg = Leg(Group(BalanceStrategy.Round) with { IsEnabled = false }, ["direct", "awgbor"]);

        Assert.False(leg.IsLive);
        Assert.Equal("balancer-off", leg.Fault?.Code);
    }

    [Fact]
    public void AnOutboundThatIsOffIsNotPickedEvenWhenItAnswers()
    {
        var group = Group(BalanceStrategy.Round) with { Members = ["awgoff", "direct"] };
        var leg = Leg(group, ["awgoff", "direct"]);

        Assert.Equal([0xA601u], leg.Exit.Marks);
        Assert.False(leg.Exit.IsSpread);
    }

    [Fact]
    public void AnOutboundThePanelDoesNotHoldIsPassedOver()
    {
        var group = Group(BalanceStrategy.Priority) with { Members = ["nowhere", "direct"] };
        var leg = Leg(group, ["direct"]);

        Assert.True(leg.IsLive);
        Assert.Equal(0xA601u, leg.Mark);
    }

    [Fact]
    public void BeforeTheHostIsReadEveryOutboundThatIsOnCounts()
    {
        var rule = RouteDefaults.Fresh("ru") with { Id = 1, Outbound = "pick", Targets = ["geoip:ru"] };
        var plan = RoutePlan.Build([rule], Ways, Index(), ["awg1"], null, [Group(BalanceStrategy.Round)], null);

        Assert.Equal([0xA602u, 0xA601u], Assert.Single(plan.Legs).Exit.Marks);
    }

    [Fact]
    public void TheHostIsReadBackAsTheOutboundsThatCarryTraffic()
    {
        var live = new BalanceLive();

        Assert.True(live.Keep(["direct"]));
        Assert.False(live.Keep(["direct"]));
        Assert.True(live.Keep(["direct", "awgbor"]));
        Assert.Equal(["awgbor", "direct"], live.Alive!.Order());

        live.Forget();

        Assert.Null(live.Alive);
    }

    private static Balancer Group(string strategy) => BalanceDefaults.Fresh("pick") with
    {
        Id = 1,
        Strategy = strategy,
        Members = ["awgbor", "direct"],
    };

    private static RouteLeg Leg(Balancer balancer, string[] alive)
    {
        var rule = RouteDefaults.Fresh("ru") with { Id = 1, Outbound = "pick", Targets = ["geoip:ru"] };

        return Assert.Single(Plan([balancer], alive, rule).Legs);
    }

    private static RoutePlan Plan(Balancer[] balancers, string[] alive, params RouteRule[] rules) =>
        RoutePlan.Build(
            rules,
            Ways,
            Index(),
            ["awg1"],
            null,
            balancers,
            new HashSet<string>(alive, StringComparer.Ordinal));

    private static GeoIndex Index()
    {
        var files = new MemoryGeoFiles();
        files.Put("ip", Ip);

        return GeoIndex.Load([new GeoSource { Name = "ip", Kind = GeoKind.Ip, Position = 1 }], files);
    }
}
