using System.Net;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class RouteProbeTests
{
    private static readonly byte[] Site = GeoBuilder.Site(("YOUTUBE", ["youtube.com"]));

    private static readonly IPAddress Ru = IPAddress.Parse("77.88.8.8");

    private static readonly IPAddress Far = IPAddress.Parse("142.250.1.1");

    private static readonly IPAddress Office = IPAddress.Parse("10.9.1.12");

    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "awgbor", Kind = OutboundKind.Wg, Mark = 0xA602, Table = 42602 },
        new() { Name = "awgoff", Kind = OutboundKind.Wg, Mark = 0xA603, Table = 42603, IsEnabled = false },
    ];

    [Fact]
    public void AnAddressInTheRangesOfARuleIsTakenByIt()
    {
        var verdict = Test(Plan(Rule(1, "77.88.8.0/24")), new RouteQuery { Addresses = [Ru] });

        Assert.Equal(RouteProbe.Out, verdict.Verdict);
        Assert.Equal(1L, verdict.Leg?.Rule.Id);
        Assert.Equal(new RouteStep(1, "r1", RouteProbe.Match, "range", "77.88.8.8"), Assert.Single(verdict.Steps));
    }

    [Fact]
    public void ANameUnderTheDomainsOfARuleIsTakenByIt()
    {
        var verdict = Test(Plan(Rule(1, "77.88.8.0/24"), Rule(2, "geosite:youtube")), new RouteQuery
        {
            Name = "www.youtube.com",
            Addresses = [Far],
        });

        Assert.Equal(2L, verdict.Leg?.Rule.Id);
        Assert.Equal([RouteProbe.Miss, RouteProbe.Match], verdict.Steps.Select(step => step.Outcome));
        Assert.Equal("name", verdict.Steps[1].Reason);
    }

    [Fact]
    public void AnAddressTheResolverLaidIntoTheSetOfARuleIsTakenByIt()
    {
        var verdict = RouteProbe.Test(
            Plan(Rule(2, "geosite:youtube")),
            new RouteQuery { Addresses = [Far] },
            (rule, address) => rule == 2 && address.Equals(Far));

        Assert.Equal("resolved", Assert.Single(verdict.Steps).Reason);
        Assert.Equal(RouteProbe.Out, verdict.Verdict);
    }

    [Fact]
    public void TrafficNoRuleTakesLeavesTheWayTheHostDoes()
    {
        var verdict = Test(Plan(Rule(1, "77.88.8.0/24")), new RouteQuery { Addresses = [Far] });

        Assert.Equal(RouteProbe.Host, verdict.Verdict);
        Assert.Null(verdict.Leg);
        Assert.Equal("target", Assert.Single(verdict.Steps).Reason);
    }

    [Fact]
    public void ARuleOffTheHostIsPassedOverWithTheReason()
    {
        var plan = Plan(
            Rule(1, "77.88.8.0/24") with { IsEnabled = false },
            Rule(2, "77.88.8.0/24") with { Outbound = "nowhere", HoldsWhenDown = false },
            Rule(3, "77.88.8.0/24"));

        var verdict = Test(plan, new RouteQuery { Addresses = [Ru] });

        Assert.Equal(3L, verdict.Leg?.Rule.Id);
        Assert.Equal(["off", "unknown-outbound", "range"], verdict.Steps.Select(step => step.Reason));
        Assert.Equal(RouteProbe.Skip, verdict.Steps[0].Outcome);
    }

    [Fact]
    public void AHeldRuleDropsTheTrafficWhileItsWayIsDown()
    {
        var verdict = Test(Plan(Rule(1, "77.88.8.0/24") with { Outbound = "awgoff" }), new RouteQuery { Addresses = [Ru] });

        Assert.Equal(RouteProbe.Held, verdict.Verdict);
    }

    [Theory]
    [InlineData(RouteAction.Block, RouteProbe.Block)]
    [InlineData(RouteAction.Direct, RouteProbe.Host)]
    public void TheActionOfTheRuleDecidesTheVerdict(string action, string expected)
    {
        var verdict = Test(Plan(Rule(1, "77.88.8.0/24") with { Action = action }), new RouteQuery { Addresses = [Ru] });

        Assert.Equal(expected, verdict.Verdict);
        Assert.Equal(1L, verdict.Leg?.Rule.Id);
    }

    [Theory]
    [InlineData("inbound")]
    [InlineData("source")]
    [InlineData("protocol")]
    [InlineData("port")]
    [InlineData("source-port")]
    public void TheConditionsOfARuleAreReadBeforeItsTargets(string reason)
    {
        var rule = reason switch
        {
            "inbound" => Rule(1, "77.88.8.0/24") with { Inbounds = ["awg2"] },
            "source" => Rule(1, "77.88.8.0/24") with { Sources = ["10.9.2.0/24"] },
            "protocol" => Rule(1, "77.88.8.0/24") with { Protocol = RouteProtocol.Udp },
            "port" => Rule(1, "77.88.8.0/24") with { Ports = ["80"] },
            _ => Rule(1, "77.88.8.0/24") with { SourcePorts = ["5000-6000"] },
        };

        var query = new RouteQuery { Addresses = [Ru], Port = 443, Sources = [Office], Inbound = "awg1" };
        var verdict = Test(Plan(rule), query);

        Assert.Equal(RouteProbe.Host, verdict.Verdict);
        Assert.Equal(reason, Assert.Single(verdict.Steps).Reason);
    }

    [Fact]
    public void ARuleWhoseConditionsHoldTakesTheTraffic()
    {
        var rule = Rule(1, "77.88.8.0/24") with
        {
            Inbounds = ["awg1"],
            Sources = ["10.9.1.0/24"],
            Protocol = RouteProtocol.Tcp,
            Ports = ["400-500"],
            SourcePorts = ["5000-6000"],
        };

        var query = new RouteQuery { Addresses = [Ru], Port = 443, SourcePort = 5555, Sources = [Office], Inbound = "awg1" };

        Assert.Equal(RouteProbe.Out, Test(Plan(rule), query).Verdict);
    }

    [Theory]
    [InlineData(853, "8.8.8.8", "dot")]
    [InlineData(443, "1.1.1.1", "doh")]
    [InlineData(443, "77.88.8.8", "")]
    public void TheGuardsOfTheResolverGoAheadOfTheRules(int port, string address, string guard)
    {
        var dns = DnsDefaults.Settings with { IsEnabled = true, BlockDot = true, BlockDoh = true };
        var plan = RoutePlan.Build([Rule(1, "0.0.0.0/0")], Ways, Index(), ["awg1"], dns);

        var verdict = Test(plan, new RouteQuery { Addresses = [IPAddress.Parse(address)], Port = port });

        Assert.Equal(guard, verdict.Guard);
        Assert.Equal(guard.Length > 0 ? RouteProbe.Guarded : RouteProbe.Out, verdict.Verdict);
    }

    [Theory]
    [InlineData("10.9.1.0/24", "10.9.1.200", true)]
    [InlineData("10.9.1.7/24", "10.9.1.200", true)]
    [InlineData("10.9.1.0/24", "10.9.2.1", false)]
    [InlineData("fd00::/64", "fd00::5", true)]
    [InlineData("fd00::/64", "10.9.1.1", false)]
    [InlineData("8.8.8.8", "8.8.8.8", true)]
    public void AnAddressFallsIntoTheRangesOfItsOwnFamily(string range, string address, bool within) =>
        Assert.Equal(within, RouteProbe.Within([range], IPAddress.Parse(address)));

    private static RouteVerdict Test(RoutePlan plan, RouteQuery query) =>
        RouteProbe.Test(plan, query, (_, _) => false);

    private static RoutePlan Plan(params RouteRule[] rules) => RoutePlan.Build(rules, Ways, Index(), ["awg1", "awg2"]);

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
