using System.Net;
using System.Net.Http.Headers;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Tests;

public class GeoStoreTests
{
    private static readonly byte[] Database = GeoBuilder.Ip(("RU", ["77.88.8.0/24"]));

    [Fact]
    public async Task AFreshInstallCarriesTheStandardSources()
    {
        using var bench = new Bench();

        var held = await bench.Geo.ListAsync(CancellationToken.None);

        Assert.Equal(GeoDefaults.Sources.Length, held.Count);
        Assert.Equal([1, 2, 3, 4, 5], held.Select(source => source.Position));
        Assert.All(held, source => Assert.True(source.IsEnabled));
        Assert.Equal(0, await bench.Geo.SeedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task APanelSeededBeforeGetsTheSourceThatJoinedOnTopOnce()
    {
        using var bench = new Bench();
        await SeededBeforeAsync(bench);

        var added = await bench.Geo.SeedAsync(CancellationToken.None);
        var held = await bench.Geo.ListAsync(CancellationToken.None);

        Assert.Equal(1, added);
        Assert.Equal(["zkeenip", "geosite", "geoip", "geosite-ru-only", "geoip-ru-only"], held.Select(source => source.Name));
        Assert.Equal([1, 2, 3, 4, 5], held.Select(source => source.Position));
        Assert.Equal(0, await bench.Geo.SeedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AStandardSourceRemovedByHandIsNotBroughtBack()
    {
        using var bench = new Bench();
        await SeededBeforeAsync(bench, "geoip-ru-only");

        var added = await bench.Geo.SeedAsync(CancellationToken.None);
        var zkeenip = (await bench.Geo.ListAsync(CancellationToken.None))[0];
        await bench.Geo.RemoveAsync(zkeenip.Id, CancellationToken.None);
        var again = await bench.Geo.SeedAsync(CancellationToken.None);

        Assert.Equal(1, added);
        Assert.Equal("zkeenip", zkeenip.Name);
        Assert.Equal(0, again);
        Assert.Equal(["geosite", "geoip", "geosite-ru-only"], (await bench.Geo.ListAsync(CancellationToken.None)).Select(source => source.Name));
    }

    [Fact]
    public async Task AStandardSourceHeldUnderItsAddressIsNotAddedTwice()
    {
        using var bench = new Bench();
        await SeededBeforeAsync(bench);
        var url = GeoDefaults.Sources.Single(source => source.Name == "zkeenip").Url;
        await bench.Geo.AddAsync(Draft("mine", url), CancellationToken.None);

        var added = await bench.Geo.SeedAsync(CancellationToken.None);
        var held = await bench.Geo.ListAsync(CancellationToken.None);

        Assert.Equal(0, added);
        Assert.Single(held, source => source.Url == url);
        Assert.DoesNotContain(held, source => source.Name == "zkeenip");
    }

    [Fact]
    public async Task ASourceThatJoinedGoesInFrontOfTheStandardOneThatFollowsIt()
    {
        using var bench = new Bench();
        await SeededBeforeAsync(bench);
        var mine = await bench.Geo.AddAsync(Draft("mine", "https://example.org/one.dat"), CancellationToken.None);
        for (var step = 0; step < 4; step++)
        {
            await bench.Geo.MoveAsync(mine.Record!.Id, up: true, CancellationToken.None);
        }

        await bench.Geo.SeedAsync(CancellationToken.None);

        Assert.Equal(
            ["mine", "zkeenip", "geosite", "geoip", "geosite-ru-only", "geoip-ru-only"],
            (await bench.Geo.ListAsync(CancellationToken.None)).Select(source => source.Name));
    }

    [Fact]
    public async Task APanelWithEverySourceRemovedStaysEmpty()
    {
        using var bench = new Bench();
        foreach (var source in await bench.Geo.ListAsync(CancellationToken.None))
        {
            await bench.Geo.RemoveAsync(source.Id, CancellationToken.None);
        }

        Assert.Equal(0, await bench.Geo.SeedAsync(CancellationToken.None));
        Assert.Empty(await bench.Geo.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ASourceUnderATakenNameOrAddressIsRefused()
    {
        using var bench = new Bench();

        var name = await bench.Geo.AddAsync(Draft("geoip", "https://example.org/one.dat"), CancellationToken.None);
        var url = await bench.Geo.AddAsync(Draft("mine", GeoDefaults.Sources[0].Url), CancellationToken.None);
        var bad = await bench.Geo.AddAsync(Draft("mine", "example.org/two.dat"), CancellationToken.None);

        Assert.Equal(GeoOutcome.NameTaken, name.Outcome);
        Assert.Equal(GeoOutcome.UrlTaken, url.Outcome);
        Assert.Equal(GeoOutcome.Invalid, bad.Outcome);
        Assert.Equal("bad-url", bad.Code);
    }

    [Fact]
    public async Task AnAddedSourceGoesAfterTheOnesAlreadyHeld()
    {
        using var bench = new Bench();

        var added = await bench.Geo.AddAsync(Draft("mine", "https://example.org/one.dat"), CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        Assert.Equal(GeoDefaults.Sources.Length + 1, added.Record!.Position);
    }

    [Fact]
    public async Task ASourceMovesThroughTheOrderAndStopsAtTheEdge()
    {
        using var bench = new Bench();
        var held = await bench.Geo.ListAsync(CancellationToken.None);
        var second = held[1];

        var up = await bench.Geo.MoveAsync(second.Id, up: true, CancellationToken.None);
        var edge = await bench.Geo.MoveAsync(up.Record!.Id, up: true, CancellationToken.None);

        Assert.Equal(1, up.Record!.Position);
        Assert.Equal(1, edge.Record!.Position);
        Assert.Equal(second.Id, (await bench.Geo.ListAsync(CancellationToken.None))[0].Id);
    }

    [Fact]
    public async Task ARemovedSourceTakesItsFileWithIt()
    {
        using var bench = new Bench();
        var source = (await bench.Geo.ListAsync(CancellationToken.None))[0];
        await bench.GeoFiles.WriteAsync(source.Name, Database, CancellationToken.None);

        await bench.Geo.RemoveAsync(source.Id, CancellationToken.None);

        Assert.Null(bench.GeoFiles.OpenRead(source.Name));
        Assert.DoesNotContain(await bench.Geo.ListAsync(CancellationToken.None), held => held.Id == source.Id);
    }

    [Fact]
    public async Task AChangedAddressDropsWhatWasDownloadedBefore()
    {
        using var bench = new Bench();
        var source = (await bench.Geo.ListAsync(CancellationToken.None))[0];
        await bench.GeoFiles.WriteAsync(source.Name, Database, CancellationToken.None);
        await bench.Geo.StampAsync(
            source.Id,
            new GeoDownload(true, bench.Clock.Now, "abc", 7, Database.Length, "\"one\"", string.Empty),
            CancellationToken.None);

        var changed = await bench.Geo.ChangeAsync(
            source.Id,
            source with { Url = "https://example.org/other.dat" },
            CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Null(changed.Record!.UpdatedUtc);
        Assert.Equal(string.Empty, changed.Record.Sha256);
        Assert.Null(bench.GeoFiles.OpenRead(source.Name));
    }

    [Fact]
    public async Task ADownloadIsWrittenDownOnTheSource()
    {
        using var bench = new Bench();
        var source = (await bench.Geo.ListAsync(CancellationToken.None))[0];

        var stamped = await bench.Geo.StampAsync(
            source.Id,
            new GeoDownload(true, bench.Clock.Now, "abc", 12, 345, "\"one\"", "Mon, 07 Sep 2026 00:00:00 GMT"),
            CancellationToken.None);
        var failed = await bench.Geo.FailAsync(source.Id, "the host did not answer", CancellationToken.None);

        Assert.Equal(bench.Clock.Now, stamped.Record!.UpdatedUtc);
        Assert.Equal(12, stamped.Record.EntryCount);
        Assert.Equal("the host did not answer", failed.Record!.LastError);
        Assert.Equal("abc", failed.Record.Sha256);
    }

    [Fact]
    public async Task ADownloadedDatabaseIsStoredWithWhatItHolds()
    {
        var files = new MemoryGeoFiles();
        var answers = new Answers(Database, "\"one\"");
        var downloader = new GeoDownloader(new HttpClient(answers), files);

        var download = await downloader.UpdateAsync(Draft("geoip", "https://example.org/one.dat"), CancellationToken.None);

        Assert.True(download.Changed);
        Assert.Equal(1, download.EntryCount);
        Assert.Equal(Database.Length, download.Size);
        Assert.Equal("\"one\"", download.ETag);
        Assert.True(files.Has("geoip"));
    }

    [Fact]
    public async Task AnUnchangedDatabaseIsNotDownloadedAgain()
    {
        var files = new MemoryGeoFiles();
        var answers = new Answers(Database, "\"one\"") { Status = HttpStatusCode.NotModified };
        var downloader = new GeoDownloader(new HttpClient(answers), files);
        var source = Draft("geoip", "https://example.org/one.dat") with { Sha256 = "abc", ETag = "\"one\"", EntryCount = 3 };

        var download = await downloader.UpdateAsync(source, CancellationToken.None);

        Assert.False(download.Changed);
        Assert.Equal(3, download.EntryCount);
        Assert.False(files.Has("geoip"));
        Assert.Equal("\"one\"", answers.Asked);
    }

    [Fact]
    public async Task AnAddressThatAnswersWithSomethingElseIsRefused()
    {
        var files = new MemoryGeoFiles();
        var downloader = new GeoDownloader(new HttpClient(new Answers("<html>not a database</html>"u8.ToArray(), string.Empty)), files);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => downloader.UpdateAsync(Draft("geoip", "https://example.org/one.dat"), CancellationToken.None));

        Assert.False(files.Has("geoip"));
    }

    private static GeoSource Draft(string name, string url) => new()
    {
        Name = name,
        Kind = GeoKind.Ip,
        Url = url,
    };

    // Turns the fresh panel of a bench into one seeded by the first set: no zkeenip, no mark of the set.
    private static async Task SeededBeforeAsync(Bench bench, params string[] removed)
    {
        var gone = await bench.Db.GeoSources
            .Where(source => source.Name == "zkeenip" || removed.Contains(source.Name))
            .ToListAsync();
        bench.Db.GeoSources.RemoveRange(gone);
        bench.Db.Seeds.RemoveRange(await bench.Db.Seeds.ToListAsync());
        await bench.Db.SaveChangesAsync();

        var position = 0;
        foreach (var source in await bench.Db.GeoSources.OrderBy(source => source.Position).ToListAsync())
        {
            source.Position = ++position;
        }

        await bench.Db.SaveChangesAsync();
    }

    private sealed class Answers : HttpMessageHandler
    {
        private readonly byte[] _body;

        private readonly string _etag;

        public Answers(byte[] body, string etag)
        {
            _body = body;
            _etag = etag;
        }

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string Asked { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked = request.Headers.IfNoneMatch.ToString();
            var response = new HttpResponseMessage(Status) { Content = new ByteArrayContent(_body) };
            if (_etag.Length > 0)
            {
                response.Headers.ETag = EntityTagHeaderValue.Parse(_etag);
            }

            return Task.FromResult(response);
        }
    }
}
