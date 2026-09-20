using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class RouteTests
{
    private static readonly byte[] Ip = GeoBuilder.Ip(
        ("RU", ["77.88.8.0/24", "2a02:6b8::/32"]),
        ("DE", ["1.2.3.0/24"]));

    private static readonly byte[] Site = GeoBuilder.Site(("YOUTUBE", ["youtube.com", "ytimg.com"]));

    [Theory]
    [InlineData("geoip:ru", GeoRuleKind.GeoIp, "ru")]
    [InlineData("GeoSite:youtube", GeoRuleKind.GeoSite, "youtube")]
    [InlineData("1.2.3.0/24", GeoRuleKind.Cidr, "1.2.3.0/24")]
    [InlineData("8.8.8.8", GeoRuleKind.Cidr, "8.8.8.8/32")]
    [InlineData("2a02:6b8::/32", GeoRuleKind.Cidr, "2a02:6b8::/32")]
    [InlineData("Youtube.com", GeoRuleKind.Domain, "youtube.com")]
    [InlineData("domain:Ifconfig.me", GeoRuleKind.Domain, "ifconfig.me")]
    [InlineData(" domain:youtube.com ", GeoRuleKind.Domain, "youtube.com")]
    [InlineData("keyword:Ads", GeoRuleKind.Keyword, "ads")]
    [InlineData(" KEYWORD:double-click ", GeoRuleKind.Keyword, "double-click")]
    public void ATargetIsReadAsWhatItLooksLike(string text, GeoRuleKind kind, string value)
    {
        var target = RouteRules.Target(text);

        Assert.NotNull(target);
        Assert.Equal(kind, target.Kind);
        Assert.Equal(value, target.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("geoip:")]
    [InlineData("domain:")]
    [InlineData("domain:no dots here")]
    [InlineData("keyword:")]
    [InlineData("keyword:two words")]
    [InlineData("1.2.3.0/44")]
    [InlineData("no dots here")]
    [InlineData(".leading.dot")]
    [InlineData("trailing.dot.")]
    public void ATargetThatMeansNothingIsRefused(string text) => Assert.Null(RouteRules.Target(text));

    [Fact]
    public void AKeywordTargetMatchesEveryNameThatCarriesIt()
    {
        var rule = RouteRules.Target("keyword:ads");
        var targets = GeoMaterializer.Materialize([rule!], GeoIndex.Load([], new MemoryGeoFiles()));
        var matcher = new DomainMatcher(targets.Domains);

        Assert.Empty(targets.Cidrs);
        Assert.Equal(GeoDomainKind.Plain, Assert.Single(targets.Domains).Kind);
        Assert.True(matcher.Matches("ads.example.com"));
        Assert.True(matcher.Matches("doubleclick-ads.net"));
        Assert.False(matcher.Matches("example.com"));
    }

    [Theory]
    [InlineData("443", 443, 443)]
    [InlineData(" 1000-2000 ", 1000, 2000)]
    public void APortRangeIsRead(string text, int low, int high)
    {
        Assert.True(RouteRules.Ports(text, out var read, out var last));
        Assert.Equal(low, read);
        Assert.Equal(high, last);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("2000-1000")]
    [InlineData("http")]
    public void APortThatIsNotOneIsRefused(string text) =>
        Assert.False(RouteRules.Ports(text, out _, out _));

    [Fact]
    public void ARuleThatNamesNoOutboundIsRefused()
    {
        var fault = RouteRules.Check(RouteDefaults.Fresh("through the tunnel"));

        Assert.Equal("no-outbound", fault?.Code);
    }

    [Fact]
    public void ARuleThatOnlyBlocksNeedsNoOutbound() =>
        Assert.Null(RouteRules.Check(RouteDefaults.Fresh("nothing") with { Action = RouteAction.Block }));

    [Theory]
    [InlineData("", "", "bad-rule-name")]
    [InlineData("a name that is very much longer than the panel takes", "", "bad-rule-name")]
    [InlineData("ok", "sideways", "bad-action")]
    public void ARuleWithBrokenSettingsNamesTheField(string name, string action, string code)
    {
        var rule = new RouteRule
        {
            Name = name,
            Action = action.Length > 0 ? action : RouteAction.Block,
        };

        Assert.Equal(code, RouteRules.Check(rule)?.Code);
    }

    [Fact]
    public void ARuleNamesTheConditionThatDoesNotHold()
    {
        var rule = Block() with { Targets = ["geoip:ru"], Sources = ["not an address"], Ports = ["443"] };

        Assert.Equal("bad-source", RouteRules.Check(rule)?.Code);
        Assert.Equal("bad-target", RouteRules.Check(rule with { Sources = [], Targets = ["what?"] })?.Code);
        Assert.Equal("bad-port", RouteRules.Check(rule with { Sources = [], Ports = ["nope"] })?.Code);
        Assert.Equal("bad-protocol", RouteRules.Check(rule with { Sources = [], Protocol = "icmp" })?.Code);
    }

    private static RouteRule Block() => RouteDefaults.Fresh("rule") with { Action = RouteAction.Block };

    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "direct", Kind = OutboundKind.Local, Mark = 0xA601, Table = OutboundRules.MainTable },
        new() { Name = "awgbor", Kind = OutboundKind.Wg, Mark = 0xA602, Table = 42602 },
        new() { Name = "awgoff", Kind = OutboundKind.Wg, Mark = 0xA603, Table = 42603, IsEnabled = false },
    ];

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

    private static RoutePlan Plan(params RouteRule[] rules) =>
        RoutePlan.Build(rules, Ways, Index(), ["awg1", "awg2"]);

    private static RouteRule Out(string outbound) =>
        RouteDefaults.Fresh("rule") with { Id = 1, Action = RouteAction.Out, Outbound = outbound };

    [Fact]
    public void AGeoTargetBecomesTheRangesTheDatabasesCarry()
    {
        var plan = Plan(Out("awgbor") with { Targets = ["geoip:ru"] });

        var leg = Assert.Single(plan.Legs);
        Assert.True(leg.IsLive);
        Assert.Equal(0xA602u, leg.Mark);
        Assert.Equal(["77.88.8.0/24"], leg.Cidrs4);
        Assert.Equal(["2a02:6b8::/32"], leg.Cidrs6);
        Assert.Contains(leg.Domains, domain => domain.Value == "xn--p1ai");
    }

    [Fact]
    public void ACategoryBecomesTheNamesTheDatabasesCarry()
    {
        var plan = Plan(Out("awgbor") with { Targets = ["geosite:youtube"] });

        var leg = Assert.Single(plan.Legs);
        Assert.Empty(leg.Cidrs4);
        Assert.Equal(["youtube.com", "ytimg.com"], leg.Domains.Select(domain => domain.Value));
    }

    [Theory]
    [InlineData("nowhere", "unknown-outbound")]
    [InlineData("awgoff", "outbound-off")]
    public void ARuleTheOutboundDoesNotBackStaysOffTheHost(string outbound, string code)
    {
        var leg = Assert.Single(Plan(Out(outbound)).Legs);

        Assert.False(leg.IsLive);
        Assert.Equal(code, leg.Fault?.Code);
    }

    [Fact]
    public void ARuleTheDatabasesCarryNothingForStaysOffTheHost()
    {
        var leg = Assert.Single(Plan(Out("awgbor") with { Targets = ["geoip:zzz"] }).Legs);

        Assert.False(leg.IsLive);
        Assert.Equal("empty-target", leg.Fault?.Code);
    }

    [Fact]
    public void TheRulesAreReadInTheOrderTheyAreHeldIn()
    {
        var plan = Plan(
            Out("direct") with { Id = 2, Name = "second", Position = 2 },
            Out("awgbor") with { Id = 1, Name = "first", Position = 1 });

        Assert.Equal(["first", "second"], plan.Legs.Select(leg => leg.Rule.Name));
    }

    [Fact]
    public void TheRulesetCarriesTheSetsTheChainsAndTheInterfacesOfTheClients()
    {
        var text = RouteRuleset.Text(Plan(Out("awgbor") with { Targets = ["geoip:ru"] }));

        Assert.Contains("table inet amneziageo_rt\ndelete table inet amneziageo_rt\n", text, StringComparison.Ordinal);
        Assert.Contains("set r1v4 {", text, StringComparison.Ordinal);
        Assert.Contains("elements = { 77.88.8.0/24 }", text, StringComparison.Ordinal);
        Assert.Contains("set r1v6 {", text, StringComparison.Ordinal);
        Assert.Contains("set n1v4 {", text, StringComparison.Ordinal);
        Assert.Contains("timeout 60m", text, StringComparison.Ordinal);
        Assert.Contains("ct mark != 0x00000000 meta mark set ct mark accept", text, StringComparison.Ordinal);
        Assert.Contains("iifname != { \"awg1\", \"awg2\" } accept", text, StringComparison.Ordinal);
        Assert.Contains("meta mark != 0x00000000 ct mark set meta mark", text, StringComparison.Ordinal);
        Assert.Contains("ip daddr @r1v4 meta mark set 0xa602 return", text, StringComparison.Ordinal);
        Assert.Contains("ip6 daddr @r1v6 meta mark set 0xa602 return", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyANewConnectionOfAClientIsAskedTheRules()
    {
        var text = RouteRuleset.Text(Plan(Out("awgbor") with { Targets = ["geoip:ru"] }));
        var clients = text.IndexOf("iifname != { \"awg1\", \"awg2\" } accept", StringComparison.Ordinal);
        var mark = text.IndexOf("ct mark != 0x00000000 meta mark set ct mark accept", StringComparison.Ordinal);
        var fresh = text.IndexOf("ct state != new accept", StringComparison.Ordinal);
        var decide = text.IndexOf("jump decide", StringComparison.Ordinal);

        Assert.True(clients >= 0 && clients < mark && mark < fresh && fresh < decide, text);
    }

    [Fact]
    public void ABrokenConnectionOfAClientIsDroppedBeforeTheMarks()
    {
        var text = RouteRuleset.Text(Plan(Out("awgbor") with { Targets = ["geoip:ru"] }));
        var clients = text.IndexOf("iifname != { \"awg1\", \"awg2\" } accept", StringComparison.Ordinal);
        var invalid = text.IndexOf("ct state invalid drop", StringComparison.Ordinal);
        var mark = text.IndexOf("ct mark != 0x00000000 meta mark set ct mark accept", StringComparison.Ordinal);

        Assert.True(clients >= 0 && clients < invalid && invalid < mark, text);
    }

    [Fact]
    public void ThePlanKeepsTheWaysItWasBuiltOver()
    {
        var plan = Plan(Out("awgbor"));

        Assert.Equal(0xA602u, plan.Ways.Outbound("awgbor")?.Mark);
        Assert.False(plan.Ways.Outbound("awgoff")?.IsEnabled);
    }

    [Fact]
    public void ARuleThatIsOffLeavesNothingOnTheHost()
    {
        var text = RouteRuleset.Text(Plan(Out("awgbor") with { Targets = ["geoip:ru"], IsEnabled = false }));

        Assert.DoesNotContain("set r1v4", text, StringComparison.Ordinal);
        Assert.DoesNotContain("meta mark set 0x", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuleHoldsItsTrafficWhileItsChannelCarriesNothing()
    {
        var plan = Plan(Out("awgoff") with { Targets = ["geoip:ru"] });
        var text = RouteRuleset.Text(plan);
        var leg = Assert.Single(plan.Legs);

        Assert.False(leg.IsLive);
        Assert.True(leg.IsHeld);
        Assert.Equal("outbound-off", leg.Fault?.Code);
        Assert.Contains("set r1v4", text, StringComparison.Ordinal);
        Assert.Contains("ip daddr @r1v4 drop", text, StringComparison.Ordinal);
        Assert.DoesNotContain("meta mark set 0x", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AChannelThePanelNoLongerHoldsAlsoHoldsTheTraffic()
    {
        var leg = Assert.Single(Plan(Out("gone") with { Targets = ["geoip:ru"] }).Legs);

        Assert.Equal("unknown-outbound", leg.Fault?.Code);
        Assert.True(leg.IsHeld);
    }

    [Fact]
    public void ARuleThatDoesNotHoldLetsItsTrafficOutOfTheHost()
    {
        var plan = Plan(Out("awgoff") with { Targets = ["geoip:ru"], HoldsWhenDown = false });
        var text = RouteRuleset.Text(plan);

        Assert.False(Assert.Single(plan.Legs).IsHeld);
        Assert.DoesNotContain("r1v4", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuleThatIsWrongItselfIsNotHeld()
    {
        var leg = Assert.Single(Plan(Out("awgbor") with { Targets = ["geosite:nothing"] }).Legs);

        Assert.Equal("empty-target", leg.Fault?.Code);
        Assert.False(leg.IsHeld);
    }

    [Fact]
    public void ARuleThatIsOffHoldsNothing()
    {
        var leg = Assert.Single(Plan(Out("awgoff") with { Targets = ["geoip:ru"], IsEnabled = false }).Legs);

        Assert.False(leg.IsHeld);
        Assert.False(leg.IsOnHost);
    }

    [Fact]
    public void WithoutAnEndpointNothingIsMarkedAtAll()
    {
        var plan = RoutePlan.Build([Out("awgbor")], Ways, Index(), []);
        var text = RouteRuleset.Text(plan);

        Assert.DoesNotContain("jump decide", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ct mark", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockingRuleDropsWhatItMatches()
    {
        var rule = RouteDefaults.Fresh("no ads") with
        {
            Id = 7,
            Action = RouteAction.Block,
            Targets = ["geosite:youtube"],
        };

        var lines = RouteRuleset.Lines(Assert.Single(Plan(rule).Legs));

        Assert.Equal(["ip daddr @n7v4 drop", "ip6 daddr @n7v6 drop"], lines);
    }

    [Theory]
    [InlineData(RouteProtocol.Any, new string[0], "meta mark set 0xa602 return")]
    [InlineData(RouteProtocol.Any, new[] { "53" }, "meta l4proto { tcp, udp } th dport { 53 } meta mark set 0xa602 return")]
    [InlineData(RouteProtocol.Tcp, new string[0], "meta l4proto tcp meta mark set 0xa602 return")]
    [InlineData(RouteProtocol.Udp, new[] { "443", "1000-2000" }, "udp dport { 443, 1000-2000 } meta mark set 0xa602 return")]
    public void TheProtocolAndThePortsTakeTheShapeTheKernelReads(string protocol, string[] ports, string line)
    {
        var rule = Out("awgbor") with { Protocol = protocol, Ports = ports };

        Assert.Equal([line], RouteRuleset.Lines(Assert.Single(Plan(rule).Legs)));
    }

    [Fact]
    public void AClientRangeIsMatchedInItsOwnFamilyOnly()
    {
        var rule = Out("awgbor") with { Sources = ["10.8.0.0/24"], Targets = ["geoip:ru"] };

        var lines = RouteRuleset.Lines(Assert.Single(Plan(rule).Legs));

        Assert.Equal(
            [
                "ip saddr { 10.8.0.0/24 } ip daddr @r1v4 meta mark set 0xa602 return",
                "ip saddr { 10.8.0.0/24 } ip daddr @n1v4 meta mark set 0xa602 return",
            ],
            lines);
    }

    [Fact]
    public void ARuleWithoutConditionsTakesOneLine()
    {
        var lines = RouteRuleset.Lines(Assert.Single(Plan(Out("direct")).Legs));

        Assert.Equal(["meta mark set 0xa601 return"], lines);
    }
}
