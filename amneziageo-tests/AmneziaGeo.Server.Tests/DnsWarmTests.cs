using System.Net;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Geo.Files;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class DnsWarmTests
{
    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "direct", Kind = OutboundKind.Local, Mark = 0xA601, Table = OutboundRules.MainTable },
        new() { Name = "off", Kind = OutboundKind.Wg, Mark = 0xA603, Table = 42603, IsEnabled = false },
    ];

    [Fact]
    public void ThePlanCarriesNoNamesWhenNoRuleMatchesByName()
    {
        Assert.Empty(DnsWarm.Names(null));
        Assert.Empty(DnsWarm.Names(Plan(Rule("1.2.3.0/24"))));
    }

    [Fact]
    public void TheNamesOfARuleComeWithTheRuleTheyFill()
    {
        var names = DnsWarm.Names(Plan(Rule("ifconfig.me")));

        var one = Assert.Single(names);
        Assert.Equal(1, one.Rule);
        Assert.Equal("ifconfig.me", one.Name);
    }

    [Fact]
    public void ACategoryGivesEveryNameItStandsFor()
    {
        var names = DnsWarm.Names(Plan(Rule("geosite:youtube")));

        Assert.Equal(["youtube.com", "ytimg.com"], names.Select(one => one.Name).Order());
    }

    [Fact]
    public void ARuleThatHoldsItsTrafficIsAskedAboutToo()
    {
        var rule = Rule("ifconfig.me") with { Outbound = "off" };
        var leg = Assert.Single(Plan(rule).Legs);

        Assert.True(leg.IsHeld);
        Assert.Single(DnsWarm.Names(Plan(rule)));
    }

    [Fact]
    public void ARuleThatIsOffIsNotAskedAbout()
    {
        Assert.Empty(DnsWarm.Names(Plan(Rule("ifconfig.me") with { IsEnabled = false })));
    }

    [Fact]
    public async Task EveryNameIsAskedAboutAndTheAddressesComeBackUnderTheirRules()
    {
        var names = DnsWarm.Names(Plan(Rule("geosite:youtube")));

        var found = await DnsWarm.AskAsync(new Book("1.2.3.4"), names, CancellationToken.None);

        Assert.Equal(2, found.Count);
        Assert.All(found, one => Assert.Equal(1, one.Name.Rule));
        Assert.All(found, one => Assert.Equal(IPAddress.Parse("1.2.3.4"), one.Address));
    }

    [Fact]
    public void ANameThatAnsweredStandsHalfTheLifetimeOfTheRecord()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), DnsWarm.Rest(TimeSpan.FromMinutes(60)));
        Assert.Equal(TimeSpan.FromSeconds(2), DnsWarm.Rest(TimeSpan.Zero));
    }

    [Fact]
    public void ANameThatMissedIsAskedAboutAgainInSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), DnsWarm.Again(1, TimeSpan.FromMinutes(60)));
        Assert.Equal(TimeSpan.FromSeconds(4), DnsWarm.Again(2, TimeSpan.FromMinutes(60)));
        Assert.Equal(TimeSpan.FromSeconds(8), DnsWarm.Again(3, TimeSpan.FromMinutes(60)));
    }

    [Fact]
    public void AMissedNameWaitsNeitherLongerThanAMinuteNorLongerThanAnAnsweredOne()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), DnsWarm.Again(9, TimeSpan.FromMinutes(60)));
        Assert.Equal(TimeSpan.FromSeconds(5), DnsWarm.Again(9, TimeSpan.FromSeconds(10)));
    }

    private static RouteRule Rule(string target) =>
        RouteDefaults.Fresh("rule") with { Id = 1, Action = RouteAction.Out, Outbound = "direct", Targets = [target] };

    private static RoutePlan Plan(RouteRule rule) => RoutePlan.Build([rule], Ways, Index(), ["awg1"]);

    private static GeoIndex Index()
    {
        var files = new MemoryGeoFiles();
        files.Put("site", GeoBuilder.Site(("YOUTUBE", ["youtube.com", "ytimg.com"])));

        return GeoIndex.Load([new GeoSource { Name = "site", Kind = GeoKind.Site, Position = 1 }], files);
    }

    private sealed class Book(string address) : IDnsUpstream
    {
        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            var asked = DnsMessage.Read(question.Span)!;
            var told = new Told(asked.Question, asked.Type, 300, address);

            return Task.FromResult<byte[]?>(DnsBuilder.Answer(asked.Id, asked.Question, asked.Type, told));
        }
    }
}
