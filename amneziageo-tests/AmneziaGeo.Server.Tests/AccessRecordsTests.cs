using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Access;

namespace AmneziaGeo.Server.Tests;

public sealed class AccessRecordsTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "access-" + Guid.NewGuid().ToString("N"));

    private readonly AccessRecords _records;

    public AccessRecordsTests()
    {
        _records = new AccessRecords(Path.Combine(_folder, AccessDefaults.FileName));
        _records.Prepare();
    }

    public void Dispose()
    {
        Pools.Clear(_records.Location);
        Directory.Delete(_folder, true);
    }

    [Fact]
    public void TheLogLivesBesideTheDatabaseOfThePanel() =>
        Assert.Equal(
            Path.Combine(Path.GetFullPath("/var/lib/amneziageo-server"), "access.db"),
            AccessRecords.PathNear("/var/lib/amneziageo-server/server.db"));

    [Fact]
    public void ThePageCarriesTheNewestRecordsFirstAndGoesOnBeforeTheLastOne()
    {
        _records.Write([.. Enumerable.Range(0, 5).Select(at => Made(at, "a.com"))]);

        var first = _records.List(Span() with { Limit = 2 });
        var second = _records.List(Span() with { Limit = 2, Before = first[^1].Id });

        Assert.Equal([Noon.AddMinutes(4), Noon.AddMinutes(3)], first.Select(record => record.At));
        Assert.Equal([Noon.AddMinutes(2), Noon.AddMinutes(1)], second.Select(record => record.At));
    }

    [Fact]
    public void ARecordWrittenLateStandsWhereItsTimePutsIt()
    {
        _records.Write([Made(5, "a.com"), Made(6, "b.com"), Made(1, "late.com")]);

        var taken = _records.List(Span());
        var paged = _records.List(Span() with { Limit = 1, Before = taken[0].Id });

        Assert.Equal(["b.com", "a.com", "late.com"], taken.Select(record => record.Name));
        Assert.Equal("a.com", Assert.Single(paged).Name);
        Assert.Equal(3, _records.Summary(Span(), AccessGrouping.Name).Total);
    }

    [Fact]
    public void ARecordComesBackAsItWasWritten()
    {
        var written = Made(1, "rr1.googlevideo.com") with
        {
            Client = "anna",
            Source = "10.8.1.2",
            SourcePort = 51234,
            Inbound = "awg1",
            Protocol = 17,
            Target = "173.194.182.8",
            Port = 443,
            Verdict = AccessVerdict.Out,
            Rule = 4,
            RuleName = "youtube",
            Way = "server2",
            Via = "pick",
            Path = AccessPath.Relay,
            Outcome = AccessOutcome.Ok,
        };

        _records.Write([written]);

        Assert.Equal(written with { Id = 1 }, Assert.Single(_records.List(Span())));
    }

    [Fact]
    public void OnlyTheRecordsOfTheSpanAreTaken()
    {
        _records.Write([.. Enumerable.Range(0, 10).Select(at => Made(at, "a.com"))]);

        var taken = _records.List(new AccessQuery { From = Noon.AddMinutes(3), To = Noon.AddMinutes(6) });

        Assert.Equal([Noon.AddMinutes(5), Noon.AddMinutes(4), Noon.AddMinutes(3)], taken.Select(record => record.At));
        Assert.Empty(_records.List(new AccessQuery { From = Noon.AddHours(1), To = Noon.AddHours(2) }));
    }

    [Fact]
    public void TheRecordsAreNarrowedByTheClientTheVerdictTheWayAndTheSearch()
    {
        _records.Write(
        [
            Made(1, "rr1.googlevideo.com") with { Client = "anna", Source = "10.8.1.2", Verdict = AccessVerdict.Out, Way = "server2" },
            Made(2, "tiktokv.com") with { Client = "anna", Source = "10.8.1.2" },
            Made(3, "ads.doubleclick.net") with { Client = "kids", Source = "10.8.1.3", Verdict = AccessVerdict.Block, RuleName = "ads" },
            Made(4, string.Empty) with { Source = "10.8.1.9", Target = "91.108.56.130" },
        ]);

        Assert.Equal(2, _records.List(Span() with { Client = "anna" }).Count);
        Assert.Equal("91.108.56.130", Assert.Single(_records.List(Span() with { Client = "10.8.1.9" })).Target);
        Assert.Equal("ads.doubleclick.net", Assert.Single(_records.List(Span() with { Verdict = AccessVerdict.Block })).Name);
        Assert.Equal("rr1.googlevideo.com", Assert.Single(_records.List(Span() with { Way = "server2" })).Name);
        Assert.Equal("tiktokv.com", Assert.Single(_records.List(Span() with { Search = "TikTok" })).Name);
        Assert.Equal("ads.doubleclick.net", Assert.Single(_records.List(Span() with { Search = "ads" })).Name);
        Assert.Equal("91.108.56.130", Assert.Single(_records.List(Span() with { Search = "91.108" })).Target);
        Assert.Empty(_records.List(Span() with { Search = "%" }));
    }

    [Fact]
    public void TheRecordsAreNarrowedByThePathAndTheOutcome()
    {
        _records.Write(
        [
            Made(1, "rr1.googlevideo.com") with { Verdict = AccessVerdict.Out, Path = AccessPath.Relay, Outcome = AccessOutcome.Ok },
            Made(2, "youtube.com") with { Path = AccessPath.Local, Outcome = AccessOutcome.Reset },
            Made(3, "ytimg.com") with { Path = AccessPath.Local, Outcome = AccessOutcome.Silent },
            Made(4, "ads.doubleclick.net") with { Verdict = AccessVerdict.Block },
        ]);

        var failed = _records.List(Span() with { Outcome = AccessOutcome.Failed });

        Assert.Equal("rr1.googlevideo.com", Assert.Single(_records.List(Span() with { Path = AccessPath.Relay })).Name);
        Assert.Equal(2, _records.List(Span() with { Path = AccessPath.Local }).Count);
        Assert.Equal("youtube.com", Assert.Single(_records.List(Span() with { Outcome = AccessOutcome.Reset })).Name);
        Assert.Equal(["ytimg.com", "youtube.com"], failed.Select(record => record.Name));
    }

    [Fact]
    public void TheSummaryCountsThePathsTheOutcomesAndWhatGotThroughInEachGroup()
    {
        _records.Write(
        [
            Made(1, "rr1.googlevideo.com") with { Verdict = AccessVerdict.Out, Path = AccessPath.Relay, Outcome = AccessOutcome.Ok },
            Made(2, "rr2.googlevideo.com") with { Verdict = AccessVerdict.Out, Path = AccessPath.Relay, Outcome = AccessOutcome.Empty },
            Made(3, "youtube.com") with { Path = AccessPath.Local, Outcome = AccessOutcome.Reset },
            Made(4, "ads.doubleclick.net") with { Verdict = AccessVerdict.Block },
        ]);

        var summary = _records.Summary(Span(), AccessGrouping.Domain);
        var video = Assert.Single(summary.Groups, group => group.Key == "googlevideo.com");
        var site = Assert.Single(summary.Groups, group => group.Key == "youtube.com");
        var ads = Assert.Single(summary.Groups, group => group.Key == "doubleclick.net");

        Assert.Equal([new AccessCount(AccessPath.Relay, 2), new AccessCount(AccessPath.Local, 1)], summary.Paths);
        Assert.Equal(3, summary.Outcomes.Sum(count => count.Count));
        Assert.DoesNotContain(summary.Outcomes, count => count.Key.Length == 0);
        Assert.Equal((1L, 1L), (video.Ok, video.Failed));
        Assert.Equal((0L, 1L), (site.Ok, site.Failed));
        Assert.Equal((0L, 0L), (ads.Ok, ads.Failed));
    }

    [Fact]
    public void TheSummaryPutsTheNamesOfADomainTogetherAndCountsTheirClients()
    {
        _records.Write(
        [
            Made(1, "rr1.googlevideo.com") with { Source = "10.8.1.2", Verdict = AccessVerdict.Out, Way = "server2", RuleName = "youtube" },
            Made(2, "rr2.googlevideo.com") with { Source = "10.8.1.3", Verdict = AccessVerdict.Out, Way = "server2", RuleName = "youtube" },
            Made(3, "rr2.googlevideo.com") with { Source = "10.8.1.3", Verdict = AccessVerdict.Out, Way = "server2", RuleName = "youtube" },
            Made(4, "tiktokv.com") with { Source = "10.8.1.2" },
            Made(5, string.Empty) with { Source = "10.8.1.2", Target = "91.108.56.130" },
        ]);

        var summary = _records.Summary(Span(), AccessGrouping.Domain);

        Assert.Equal(5, summary.Total);
        Assert.False(summary.IsCut);
        Assert.Equal([new AccessCount(AccessVerdict.Out, 3), new AccessCount(AccessVerdict.Host, 2)], summary.Verdicts);
        Assert.Equal([new AccessCount("server2", 3)], summary.Ways);
        var video = summary.Groups[0];
        Assert.Equal(
            new AccessGroup("googlevideo.com", AccessVerdict.Out, "server2", string.Empty, "youtube", 2, 3, 0, 0, Noon.AddMinutes(1), Noon.AddMinutes(3)),
            video);
        Assert.Contains(summary.Groups, group => group.Key == "91.108.56.130" && group.Count == 1);
        Assert.Contains(summary.Groups, group => group.Key == "tiktokv.com" && group.Verdict == AccessVerdict.Host);
    }

    [Fact]
    public void TheSummaryKeepsTheWaysOfOneNameApart()
    {
        _records.Write(
        [
            Made(1, "tiktokv.com") with { Verdict = AccessVerdict.Out, Way = "server2", RuleName = "tiktok" },
            Made(2, "tiktokv.com"),
            Made(3, "tiktokv.com"),
        ]);

        var groups = _records.Summary(Span(), AccessGrouping.Name).Groups;

        Assert.Equal(2, groups.Count);
        Assert.Equal((AccessVerdict.Host, 2L), (groups[0].Verdict, groups[0].Count));
        Assert.Equal((AccessVerdict.Out, 1L), (groups[1].Verdict, groups[1].Count));
    }

    [Fact]
    public void TheSummaryPutsTheRecordsTogetherByTheRuleTheWayOrTheClient()
    {
        _records.Write(
        [
            Made(1, "a.com") with { Client = "anna", Source = "10.8.1.2", Verdict = AccessVerdict.Out, Way = "server2", RuleName = "youtube" },
            Made(2, "b.com") with { Client = "anna", Source = "10.8.1.2", Verdict = AccessVerdict.Out, Way = "server2", RuleName = "youtube" },
            Made(3, "c.com") with { Source = "10.8.1.9" },
        ]);

        Assert.Equal(["youtube", string.Empty], _records.Summary(Span(), AccessGrouping.Rule).Groups.Select(group => group.Key));
        Assert.Equal(["server2", string.Empty], _records.Summary(Span(), AccessGrouping.Way).Groups.Select(group => group.Key));
        Assert.Equal(["anna", "10.8.1.9"], _records.Summary(Span(), AccessGrouping.Client).Groups.Select(group => group.Key));
    }

    [Fact]
    public void AnEmptySpanSumsToNothing()
    {
        var summary = _records.Summary(Span(), AccessGrouping.Domain);

        Assert.Equal(0, summary.Total);
        Assert.Empty(summary.Groups);
    }

    [Fact]
    public void TheOldRecordsAndThoseOverTheCountGo()
    {
        _records.Write([.. Enumerable.Range(0, 10).Select(at => Made(at, "a.com"))]);

        var old = _records.Trim(Noon.AddMinutes(3), 100);
        var over = _records.Trim(Noon, 4);
        var stock = _records.Stock();

        Assert.Equal(3, old);
        Assert.Equal(3, over);
        Assert.Equal(4, stock.Records);
        Assert.Equal(Noon.AddMinutes(6), stock.Oldest);
        Assert.Equal(Noon.AddMinutes(9), stock.Newest);
        Assert.True(stock.Bytes > 0);
    }

    [Fact]
    public void ClearingLeavesNoRecord()
    {
        _records.Write([Made(1, "a.com")]);

        _records.Clear();
        var stock = _records.Stock();

        Assert.Equal(new AccessStock(0, null, null, stock.Bytes), stock);
    }

    private static AccessQuery Span() => new() { From = Noon.AddHours(-1), To = Noon.AddHours(1) };

    private static AccessRecord Made(int minute, string name) => new()
    {
        At = Noon.AddMinutes(minute),
        Source = "10.8.1.2",
        Target = "142.250.1.1",
        Port = 443,
        Protocol = 6,
        Name = name,
        Verdict = AccessVerdict.Host,
    };
}
