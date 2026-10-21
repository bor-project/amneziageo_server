using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class DnsNeedTests
{
    private static readonly byte[] Site = GeoBuilder.Site(("YOUTUBE", ["youtube.com"]));

    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "awgbor", Kind = OutboundKind.Wg, Mark = 0xA602, Table = 42602 },
    ];

    private static readonly DnsSettings On = DnsDefaults.Settings with { IsEnabled = true };

    private static readonly ServerConfig Endpoint = new()
    {
        Id = 1,
        Name = "awg1",
        Address = ["10.8.0.1/24", "fd00::1/64"],
        Dns = ["1.1.1.1"],
        PrivateKey = "kEnCIRhKjNllB2d7J7t5gMAySOXbC9KKrcPwIbkgGUc=",
        PublicKey = "6isSFA1FOVjJl9Ub1VvoJ8OKXFgLPFcwJjzLU+AWSlE=",
    };

    [Theory]
    [InlineData("geosite:youtube", true)]
    [InlineData("youtube.com", true)]
    [InlineData("77.88.8.0/24", false)]
    public void TheRoutingNeedsTheResolverForARuleByName(string target, bool needed) =>
        Assert.Equal(needed, DnsNeed.Of(Plan(On, Rule(1, target)), false));

    [Fact]
    public void ARuleByNameThatIsOffNeedsNoResolver() =>
        Assert.False(DnsNeed.Of(Plan(On, Rule(1, "youtube.com") with { IsEnabled = false }), false));

    [Fact]
    public void ATemplateWithStandInAddressesNeedsTheResolver()
    {
        Assert.True(DnsNeed.Of(Plan(On, Rule(1, "77.88.8.0/24")), true));
        Assert.True(DnsNeed.Of(null, true));
        Assert.False(DnsNeed.Of(null, false));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void TheResolverRunsWhileThePanelAllowsItAndTheRoutingNeedsIt(bool allowed, bool needed, bool runs) =>
        Assert.Equal(runs, DnsNeed.Running(DnsDefaults.Settings with { IsEnabled = allowed }, needed).IsEnabled);

    [Fact]
    public void QuestionsAreTurnedToTheResolverOnlyOnTheInterfacesItAnswers()
    {
        var resolve = Chain(RouteRuleset.Text(Plan(On, Rule(1, "youtube.com")) with { Unanswered = ["awg2"] }), "resolve");

        Assert.Contains("iifname != { \"awg1\" } accept", resolve);
        Assert.DoesNotContain("awg2", resolve);
    }

    [Fact]
    public void NoQuestionIsTurnedWhileTheFirewallKeepsEveryInterfaceFromTheResolver()
    {
        var text = RouteRuleset.Text(Plan(On, Rule(1, "youtube.com")) with { Unanswered = ["awg1", "awg2"] });

        Assert.DoesNotContain("chain resolve", text);
    }

    [Fact]
    public void TheGuardsStandOnlyOnTheInterfacesTheResolverAnswers()
    {
        var plan = Plan(On, Rule(1, "geosite:youtube"));

        Assert.False(RouteRuleset.Guards(plan with { Unanswered = ["awg1", "awg2"] }));
        Assert.Equal("iifname != { \"awg1\" } accept", RouteRuleset.Guard(plan with { Unanswered = ["awg2"] })[0]);
    }

    [Fact]
    public void AConfigurationTheFirewallKeepsFromTheResolverIsHandedNoResolver()
    {
        Assert.Empty(DnsHandout.For(Endpoint, On, ["awg1"]));
        Assert.Equal(["10.8.0.1", "fd00::1"], DnsHandout.For(Endpoint, On, ["awg2"]));
    }

    [Theory]
    [InlineData("awg1", "dns")]
    [InlineData("awg2", "out")]
    public void AQuestionFromAnInterfaceTheResolverDoesNotAnswerGoesItsOwnWay(string inbound, string expected)
    {
        var plan = Plan(On, Rule(1, "0.0.0.0/0")) with { Unanswered = ["awg2"] };

        var verdict = RouteProbe.Test(
            plan,
            new RouteQuery { Addresses = [System.Net.IPAddress.Parse("77.88.8.8")], Port = 53, Inbound = inbound },
            (_, _) => false);

        Assert.Equal(expected, verdict.Verdict);
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(false, 2, true)]
    [InlineData(false, 0, false)]
    public void TheResolverIsKeptWhileTheRoutingNeedsItOrClientsHoldFilesThatNameIt(bool needed, int holders, bool kept) =>
        Assert.Equal(kept, DnsNeed.Kept(needed, holders));

    [Fact]
    public void ClientsAreHandedTheResolverOnlyWhileTheRoutingNeedsIt()
    {
        var state = new DnsState();
        state.Took(On, On);
        state.Hold(3);

        state.Need(false);
        var spare = DnsHandout.For(Endpoint, state.Handing!);
        state.Need(true);
        var needed = DnsHandout.For(Endpoint, state.Handing!);

        Assert.Empty(spare);
        Assert.Equal(["10.8.0.1", "fd00::1"], needed);
        Assert.True(state.Settings!.IsEnabled);
    }

    [Fact]
    public void AFileNamesTheResolverUnlessItsTemplateNamesServersOfItsOwn()
    {
        IReadOnlyList<string> resolver = ["10.8.0.1"];
        var client = ClientDefaults.Fresh(1, "anna") with { Address = ["10.8.0.2/32"] };
        ClientTemplate?[] templates =
        [
            null,
            new ClientTemplate { Name = "bare" },
            new ClientTemplate { Name = "own", Dns = ["9.9.9.9"] },
            new ClientTemplate { Name = "names", Dns = ["9.9.9.9"], StandIns = true },
        ];

        var written = templates
            .Select(template => ClientText.Text(Endpoint, client, template, resolver).Contains("DNS = 10.8.0.1", StringComparison.Ordinal))
            .ToArray();
        var told = templates.Select(template => ClientText.NamesResolver(template, resolver)).ToArray();

        Assert.Equal([true, true, false, true], written);
        Assert.Equal(written, told);
        Assert.False(ClientText.NamesResolver(null, []));
    }

    private static string Chain(string text, string name)
    {
        var start = text.IndexOf($"chain {name} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"there is no chain {name}");

        return text[start..text.IndexOf("\n\t}", start, StringComparison.Ordinal)];
    }

    private static RoutePlan Plan(DnsSettings dns, params RouteRule[] rules) =>
        RoutePlan.Build(rules, Ways, Index(), ["awg1", "awg2"], dns);

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
