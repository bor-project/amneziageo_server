using System.Net;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class DnsPlanTests
{
    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "direct", Kind = OutboundKind.Local, Mark = 0xA601, Table = OutboundRules.MainTable },
    ];

    private static readonly DnsSettings On = DnsDefaults.Settings with { IsEnabled = true, Port = 5300 };

    [Fact]
    public void AnAnswerIsGivenBackUnderTheNumberOfTheAsking()
    {
        var clock = new Clock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        var cache = new DnsCache(16, clock);
        var answer = DnsBuilder.Answer(5, "a.test", DnsRecordType.A, new Told("a.test", DnsRecordType.A, 60, "1.2.3.4"));

        cache.Keep("a.test", DnsRecordType.A, answer, TimeSpan.FromMinutes(5));
        var held = cache.Take("a.test", DnsRecordType.A, 9);

        Assert.NotNull(held);
        Assert.Equal(9, DnsMessage.Read(held)!.Id);
    }

    [Fact]
    public void AnAnswerThatRanOutIsNotGivenBack()
    {
        var clock = new Clock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        var cache = new DnsCache(16, clock);
        cache.Keep("a.test", DnsRecordType.A, DnsBuilder.Question(1, "a.test"), TimeSpan.FromMinutes(1));

        clock.Pass(TimeSpan.FromMinutes(2));

        Assert.Null(cache.Take("a.test", DnsRecordType.A, 1));
    }

    [Fact]
    public void ACacheOfNoAnswersHoldsNothing()
    {
        var cache = new DnsCache(0);
        cache.Keep("a.test", DnsRecordType.A, DnsBuilder.Question(1, "a.test"), TimeSpan.FromMinutes(1));

        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Take("a.test", DnsRecordType.A, 1));
    }

    [Fact]
    public void ACacheKeepsToItsSize()
    {
        var cache = new DnsCache(4);
        for (var i = 0; i < 12; i++)
        {
            cache.Keep($"name{i}.test", DnsRecordType.A, DnsBuilder.Question(1, "a.test"), TimeSpan.FromMinutes(5));
        }

        Assert.True(cache.Count <= 4, $"the cache holds {cache.Count} answers");
    }

    [Fact]
    public void ANameFallsUnderTheRuleThatMatchesIt()
    {
        var names = DnsNames.Build(Plan(Rule("youtube.com")));

        Assert.Equal(1, names.Count);
        Assert.Equal([1L], names.Match("www.youtube.com"));
        Assert.Empty(names.Match("example.com"));
    }

    [Fact]
    public void ARuleThatIsOffMatchesNoName()
    {
        var plan = Plan(Rule("youtube.com") with { IsEnabled = false });

        Assert.Equal(0, DnsNames.Build(plan).Count);
    }

    [Fact]
    public void TheAddressesGoIntoTheSetsOfTheirRules()
    {
        var text = DnsSets.Text(
            [
                new DnsEntry(1, IPAddress.Parse("1.2.3.4")),
                new DnsEntry(1, IPAddress.Parse("5.6.7.8")),
                new DnsEntry(2, IPAddress.Parse("2a02:6b8::1")),
            ],
            TimeSpan.FromMinutes(30));

        Assert.Contains("add element inet amneziageo_rt n1v4 { 1.2.3.4 timeout 1800s, 5.6.7.8 timeout 1800s }", text, StringComparison.Ordinal);
        Assert.Contains("add element inet amneziageo_rt n2v6 { 2a02:6b8::1 timeout 1800s }", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAddressAlreadyOnTheHostIsNotSentAgain()
    {
        var clock = new Clock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        var host = new Ledger();
        var sets = new DnsSets(host, clock);
        var plan = Plan(Rule("youtube.com"));

        sets.Add(1, IPAddress.Parse("1.2.3.4"), TimeSpan.FromMinutes(60));
        sets.Add(1, IPAddress.Parse("1.2.3.4"), TimeSpan.FromMinutes(60));

        Assert.Equal(1, sets.Waiting);
        Assert.Equal(1, await sets.FlushAsync(plan, TimeSpan.FromMinutes(60), CancellationToken.None));
        Assert.Equal(1, sets.Added);
        Assert.Contains("n1v4", host.Ruleset, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAddressOfARuleThatIsGoneIsDropped()
    {
        var sets = new DnsSets(new Ledger());
        sets.Add(9, IPAddress.Parse("1.2.3.4"), TimeSpan.FromMinutes(60));

        Assert.Equal(0, await sets.FlushAsync(Plan(Rule("youtube.com")), TimeSpan.FromMinutes(60), CancellationToken.None));
    }

    [Fact]
    public void AResolverThatIsOffLeavesTheRulesetAsItWas()
    {
        var text = RouteRuleset.Text(Plan(Rule("youtube.com")));

        Assert.Empty(RouteRuleset.Guard(DnsDefaults.Settings));
        Assert.DoesNotContain("chain resolve", text, StringComparison.Ordinal);
        Assert.DoesNotContain("doh4", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AResolverThatIsOnTakesTheQuestionsOfTheClients()
    {
        var text = RouteRuleset.Text(Plan(Rule("youtube.com"), On));

        Assert.Contains("chain resolve", text, StringComparison.Ordinal);
        Assert.Contains("type nat hook prerouting priority dstnat", text, StringComparison.Ordinal);
        Assert.Contains("meta l4proto { tcp, udp } th dport 53 redirect to :5300", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheQuestionsAreLeftAloneWhenTheInterceptIsOff()
    {
        var text = RouteRuleset.Text(Plan(Rule("youtube.com"), On with { Intercept = false }));

        Assert.DoesNotContain("chain resolve", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheClientsAreKeptOffTheNameServersOfTheirOwn()
    {
        var text = RouteRuleset.Text(Plan(Rule("youtube.com"), On));

        Assert.Contains("meta l4proto { tcp, udp } th dport 853 drop", text, StringComparison.Ordinal);
        Assert.Contains("ip daddr @doh4 meta l4proto { tcp, udp } th dport 443 drop", text, StringComparison.Ordinal);
        Assert.Contains("set doh6", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyWhatIsAskedForIsBlocked()
    {
        var lines = RouteRuleset.Guard(On with { BlockDoh = false });

        Assert.Equal(["meta l4proto { tcp, udp } th dport 853 drop"], lines);
    }

    [Fact]
    public void TheAddressesOfARuleLiveAsLongAsTheSettingsSay()
    {
        var text = RouteRuleset.Text(Plan(Rule("youtube.com"), On with { NameMinutes = 15 }));

        Assert.Contains("timeout 15m", text, StringComparison.Ordinal);
    }

    private static RouteRule Rule(string target) =>
        RouteDefaults.Fresh("rule") with { Id = 1, Action = RouteAction.Out, Outbound = "direct", Targets = [target] };

    private static RoutePlan Plan(RouteRule rule, DnsSettings? dns = null) =>
        RoutePlan.Build([rule], Ways, GeoIndex.Load([], new MemoryGeoFiles()), ["awg1"], dns);
}
