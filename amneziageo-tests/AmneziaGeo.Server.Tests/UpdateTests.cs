using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AmneziaGeo.Server.Api.Updates;
using AmneziaGeo.Server.Core.Panel;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class UpdateTests : IDisposable
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private const string Data = "/var/lib/amneziageo-server";

    private const string Socket = "/var/run/docker.sock";

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-update-");

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void AManifestReadsWhole()
    {
        var manifest = UpdateManifest.Parse(Manifest("1.0.1.0"));

        Assert.Equal(new Version(1, 0, 1, 0), manifest.Version);
        Assert.Equal("stable", manifest.Channel);
        Assert.Equal("ghcr.io/bor-project/amneziageo-server@sha256:" + Digest, manifest.Image);
        Assert.Equal("amneziageo-server-1.0.1.0-linux-arm64.tar.gz", manifest.PackageFor("arm64")?.Name);
        Assert.Equal(1000, manifest.PackageFor("x64")?.Size);
        Assert.Null(manifest.PackageFor("riscv64"));
    }

    [Theory]
    [InlineData("{\"version\":\"x\"}")]
    [InlineData("{\"version\":\"1.0.1.0\",\"image\":\"ghcr.io/bor-project/amneziageo-server:latest\"}")]
    [InlineData("{\"version\":\"1.0.1.0\",\"packages\":[{\"name\":\"../x.tar.gz\",\"arch\":\"x64\",\"sha256\":\"" + Digest + "\"}]}")]
    [InlineData("{\"version\":\"1.0.1.0\",\"packages\":[{\"name\":\"a.tar.gz\",\"arch\":\"x64\",\"sha256\":\"00\"}]}")]
    [InlineData("{\"version\":\"1.0.1.0\",\"packages\":[{\"name\":\"a.tar.gz\",\"sha256\":\"" + Digest + "\"}]}")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public void AManifestThatDoesNotHoldTogetherIsRefused(string json)
    {
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void ASignatureMadeWithTheKeyHolds()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = Manifest("1.0.1.0");

        Assert.True(UpdateSignature.Holds(data, Sign(key, data), key.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void ASignatureOfAnotherKeyOrOverOtherDataFails()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = Manifest("1.0.1.0");
        var signature = Sign(key, data);

        Assert.False(UpdateSignature.Holds(data, signature, other.ExportSubjectPublicKeyInfoPem()));
        Assert.False(UpdateSignature.Holds(Manifest("1.0.2.0"), signature, key.ExportSubjectPublicKeyInfoPem()));
        Assert.False(UpdateSignature.Holds(data, [1, 2, 3], key.ExportSubjectPublicKeyInfoPem()));
        Assert.Throws<InvalidDataException>(() => UpdateSignature.Holds(data, signature, "no key"));
    }
    [Fact]
    public void ThePublishedKeyReadsAsAP256Key()
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(UpdateSignature.Published);

        Assert.Equal(256, key.KeySize);
        Assert.False(UpdateSignature.Holds(Manifest("1.0.1.0"), [1, 2, 3], UpdateSignature.Published));
    }


    [Fact]
    public void TheNewestReleaseOfTheChannelWithASignedManifestIsPicked()
    {
        var releases = Releases();

        var stable = UpdateFeed.Pick(releases, tests: false);
        var test = UpdateFeed.Pick(releases, tests: true);

        Assert.Equal(new Version(1, 0, 1, 0), stable?.Version);
        Assert.Equal(
            "https://github.com/bor-project/amneziageo_server/releases/download/v1.0.1.0/update.json",
            stable?.Manifest.AbsoluteUri);
        Assert.Equal("https://github.com/bor-project/amneziageo_server/releases/tag/v1.0.1.0", stable?.Notes);
        Assert.Equal(new Version(1, 0, 1, 3), test?.Version);
        Assert.Null(UpdateFeed.Pick(Encoding.UTF8.GetBytes("[]"), tests: true));
    }

    [Theory]
    [InlineData("v1.0.1.0", "1.0.1.0")]
    [InlineData("1.2.3.4-rc1", "1.2.3.4")]
    [InlineData("latest", null)]
    public void TheVersionIsReadOutOfTheTag(string tag, string? version)
    {
        Assert.Equal(version, UpdateFeed.VersionOf(tag)?.ToString());
    }

    [Fact]
    public async Task AManifestReadOnItsOwnIsCheckedAndOffered()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = Manifest("1.0.1.0");
        using var http = new HttpClient(new Files(Published(data, Sign(key, data))));
        var feed = new UpdateFeed(http, new UpdateOptions { Manifest = "https://releases.test/1.0.1.0/update.json" }, tests: false);

        var offer = await feed.NewestAsync(key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None);

        Assert.Equal(new Version(1, 0, 1, 0), offer?.Manifest.Version);
        Assert.Equal("https://releases.test/1.0.1.0/", offer?.Files.AbsoluteUri);
    }

    [Fact]
    public async Task AManifestSignedWithAnotherKeyIsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = Manifest("1.0.1.0");
        using var http = new HttpClient(new Files(Published(data, Sign(other, data))));
        var feed = new UpdateFeed(http, new UpdateOptions { Manifest = "https://releases.test/1.0.1.0/update.json" }, tests: false);

        await Assert.ThrowsAsync<InvalidDataException>(() => feed.NewestAsync(key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None));
    }

    [Fact]
    public async Task ABuildBeforeAReleaseIsLeftToTheTestChannel()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = Manifest("1.0.1.3", "test");
        using var http = new HttpClient(new Files(Published(data, Sign(key, data))));
        var address = "https://releases.test/1.0.1.0/update.json";

        var stable = await new UpdateFeed(http, new UpdateOptions { Manifest = address }, tests: false)
            .NewestAsync(key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None);
        var test = await new UpdateFeed(http, new UpdateOptions { Manifest = address }, tests: true)
            .NewestAsync(key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None);

        Assert.Null(stable);
        Assert.Equal(new Version(1, 0, 1, 3), test?.Manifest.Version);
    }

    [Fact]
    public void TheStableChannelAsksForTheLatestReleaseAloneAndTheTestChannelForTheNewestFew()
    {
        Assert.Equal(
            "https://api.github.com/repos/bor-project/amneziageo_server/releases/latest",
            UpdateFeed.Address("bor-project/amneziageo_server", tests: false).AbsoluteUri);
        Assert.Equal(
            "https://api.github.com/repos/bor-project/amneziageo_server/releases?per_page=5",
            UpdateFeed.Address("bor-project/amneziageo_server", tests: true).AbsoluteUri);
    }

    [Fact]
    public void OneReleaseReadOnItsOwnIsPickedTheWayAListIs()
    {
        Assert.Equal(new Version(1, 0, 1, 0), UpdateFeed.Pick(One("v1.0.1.0", early: false), tests: false)?.Version);
        Assert.Null(UpdateFeed.Pick(One("v1.0.1.3", early: true), tests: false));
        Assert.Equal(new Version(1, 0, 1, 3), UpdateFeed.Pick(One("v1.0.1.3", early: true), tests: true)?.Version);
        Assert.Throws<InvalidDataException>(() => UpdateFeed.Pick(Encoding.UTF8.GetBytes("\"latest\""), tests: false));
    }

    [Fact]
    public async Task TheLatestReleaseIsAskedForPackedAndReadUnpacked()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var github = new GitHub(key, (_, _) => Packed(One("v1.0.1.0", early: false)));
        using var http = new HttpClient(github);
        var feed = new UpdateFeed(http, new UpdateOptions(), tests: false, Quick);

        var offer = await feed.NewestAsync(key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None);

        Assert.Equal(new Version(1, 0, 1, 0), offer?.Manifest.Version);
        Assert.Equal("https://api.github.com/repos/bor-project/amneziageo_server/releases/latest", github.Address);
        Assert.Equal("gzip", github.Packing);
        Assert.Equal(1, github.Asked);
    }

    [Fact]
    public async Task AnAnswerThatDoesNotComeInTimeIsAskedForAgain()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var github = new GitHub(key, (asked, _) => asked == 1 ? null : Plain(One("v1.0.1.0", early: false)));
        using var http = new HttpClient(github);
        var feed = new UpdateFeed(http, new UpdateOptions(), tests: false, Quick);

        var offer = await feed.NewestAsync(key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None);

        Assert.Equal(new Version(1, 0, 1, 0), offer?.Manifest.Version);
        Assert.Equal(2, github.Asked);
    }

    [Fact]
    public async Task AFaultOfTheFarSideIsAskedAboutAgain()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var github = new GitHub(key, (asked, _) =>
            asked == 1 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Plain(One("v1.0.1.0", early: false)));
        using var http = new HttpClient(github);
        var feed = new UpdateFeed(http, new UpdateOptions(), tests: false, Quick);

        var offer = await feed.NewestAsync(key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None);

        Assert.Equal(new Version(1, 0, 1, 0), offer?.Manifest.Version);
        Assert.Equal(2, github.Asked);
    }

    [Fact]
    public async Task ARefusalIsNotAskedAboutAgain()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var github = new GitHub(key, (_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var http = new HttpClient(github);
        var feed = new UpdateFeed(http, new UpdateOptions(), tests: false, Quick);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => feed.NewestAsync(key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None));

        Assert.EndsWith("answered 403", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, github.Asked);
    }

    [Fact]
    public async Task WhenNoAnswerComesTheFailureNamesTheHostTheTimeAndTheTries()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var github = new GitHub(key, (_, _) => null);
        using var http = new HttpClient(github);
        var patience = new UpdatePatience(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(10), 3);
        var feed = new UpdateFeed(http, new UpdateOptions(), tests: false, patience);

        var failure = await Assert.ThrowsAsync<TimeoutException>(
            () => feed.NewestAsync(key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None));

        Assert.StartsWith("api.github.com did not answer in ", failure.Message, StringComparison.Ordinal);
        Assert.EndsWith(" s, asked 3 time(s)", failure.Message, StringComparison.Ordinal);
        Assert.Equal(3, github.Asked);
    }

    [Fact]
    public async Task TheLookKeepsToItsWholeTimeHoweverManyTriesAreLeft()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var github = new GitHub(key, (_, _) => null);
        using var http = new HttpClient(github);
        var patience = new UpdatePatience(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), 500);
        var feed = new UpdateFeed(http, new UpdateOptions(), tests: false, patience);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(
            () => feed.NewestAsync(key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None));

        Assert.InRange(github.Asked, 2, 20);
        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task TheLookStopsWhenItsCallerDoes()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var github = new GitHub(key, (_, _) => null);
        using var http = new HttpClient(github);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var patience = new UpdatePatience(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(10), 3);
        var feed = new UpdateFeed(http, new UpdateOptions(), tests: false, patience);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => feed.NewestAsync(key.ExportSubjectPublicKeyInfoPem(), stop.Token));

        Assert.Equal(1, github.Asked);
    }

    [Fact]
    public async Task TheSettingsOfThePanelTakeTheBuildsBeforeARelease()
    {
        using var bench = new Bench();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = Manifest("1.0.1.3", "test");
        using var files = new Files(Published(data, Sign(key, data)));
        var pem = Path.Combine(_folder.FullName, "key.pem");
        File.WriteAllText(pem, key.ExportSubjectPublicKeyInfoPem());
        var options = new UpdateOptions
        {
            Manifest = "https://releases.test/1.0.1.0/update.json",
            Key = pem,
            Directory = Path.Combine(_folder.FullName, "update"),
        };
        var center = new UpdateCenter(options, new Clients(files), bench.Clock, NullLogger<UpdateCenter>.Instance, bench.Scopes);

        var stable = await center.CheckAsync(CancellationToken.None);
        await bench.Panel.SaveAsync(PanelDefaults.Settings with { Prereleases = true }, CancellationToken.None);
        var test = await center.CheckAsync(CancellationToken.None);
        await bench.Panel.SaveAsync(PanelDefaults.Settings, CancellationToken.None);
        var back = await center.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateOptions.Stable, stable.Channel);
        Assert.Null(stable.Latest);
        Assert.Equal(UpdateOptions.Test, test.Channel);
        Assert.Equal("1.0.1.3", test.Latest?.Version);
        Assert.Equal(UpdateOptions.Stable, back.Channel);
        Assert.Null(back.Latest);
    }

    [Fact]
    public async Task AStaleLookIsTakenAgainBeforeAnUpdateWhichMovesToTheNewestRelease()
    {
        using var bench = new Bench();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var older = Manifest("1.0.1.3");
        var files = Published(older, Sign(key, older));
        using var handler = new Files(files);
        var pem = Path.Combine(_folder.FullName, "key.pem");
        File.WriteAllText(pem, key.ExportSubjectPublicKeyInfoPem());
        var options = new UpdateOptions
        {
            Manifest = "https://releases.test/1.0.1.0/update.json",
            Key = pem,
            Directory = Path.Combine(_folder.FullName, "update"),
        };
        var center = new UpdateCenter(options, new Clients(handler), bench.Clock, NullLogger<UpdateCenter>.Instance, bench.Scopes);
        await center.CheckAsync(CancellationToken.None);
        var newer = Manifest("1.0.1.5");
        files["https://releases.test/1.0.1.0/update.json"] = newer;
        files["https://releases.test/1.0.1.0/update.json.sig"] = Sign(key, newer);

        await center.RefreshAsync(CancellationToken.None);
        var kept = center.Status().Latest?.Version;
        bench.Clock.Pass(UpdateCenter.Fresh);
        await center.RefreshAsync(CancellationToken.None);

        Assert.Equal("1.0.1.3", kept);
        Assert.Equal("1.0.1.5", center.Status().Latest?.Version);
        Assert.Equal("update-blocked", center.Apply("1.0.1.3")?.Error);
        Assert.Equal("update-blocked", center.Apply("1.0.1.5")?.Error);
        Assert.Equal("update-unknown", center.Apply("1.0.1.9")?.Error);
        Assert.Equal("update-unknown", center.Apply("latest")?.Error);
    }

    [Fact]
    public async Task APackageIsDownloadedCheckedAndUnpacked()
    {
        var archive = Package();
        var offer = Offer(archive, Convert.ToHexStringLower(SHA256.HashData(archive)));
        using var http = new HttpClient(new Files(new Dictionary<string, byte[]>
        {
            ["https://releases.test/r/amneziageo-server-1.0.1.0-linux-x64.tar.gz"] = archive,
        }));

        var installer = await new PackageUpdater(http).StageAsync(offer, "x64", [], _folder.FullName, _ => { }, CancellationToken.None);

        Assert.Equal(Path.Combine(_folder.FullName, "stage-1.0.1.0", "package", "amneziageo-server", "install.sh"), installer);
        Assert.Equal("#!/bin/sh\n", File.ReadAllText(installer));
        Assert.False(File.Exists(Path.Combine(_folder.FullName, "stage-1.0.1.0", "amneziageo-server-1.0.1.0-linux-x64.tar.gz")));
    }

    [Fact]
    public async Task APackageThatDoesNotMatchItsDigestIsRefused()
    {
        var archive = Package();
        using var http = new HttpClient(new Files(new Dictionary<string, byte[]>
        {
            ["https://releases.test/r/amneziageo-server-1.0.1.0-linux-x64.tar.gz"] = archive,
        }));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PackageUpdater(http).StageAsync(Offer(archive, Digest), "x64", [], _folder.FullName, _ => { }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PackageUpdater(http).StageAsync(Offer(archive, Digest), "riscv64", [], _folder.FullName, _ => { }, CancellationToken.None));
    }

    [Fact]
    public void TheInstallerRunsApartAndWritesDownHowItEnded()
    {
        var words = PackageUpdater.Words("/u/stage/install.sh", "/u/1.0.1.0.log", "/u/1.0.1.0.rc", "amneziageo-server-update-1.0.1.0");

        Assert.Equal("--unit=amneziageo-server-update-1.0.1.0", words[0]);
        Assert.Contains("--collect", words);
        Assert.Equal(new[] { "/u/stage/install.sh", "/u/1.0.1.0.log", "/u/1.0.1.0.rc" }, words.TakeLast(3));
    }

    [Fact]
    public void AnUpdateRunsUntilItsToolWritesHowItEnded()
    {
        var journal = new UpdateJournal(_folder.FullName);
        var started = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
        journal.Begin(new UpdateRecord("1.0.0.2", "1.0.1.0", UpdateModes.Docker, started));
        journal.Note("1.0.1.0", "pulling");

        var running = journal.Read(new Version(1, 0, 0, 2), started.AddMinutes(1));

        Assert.Equal(UpdateJournal.Running, running?.State);
        Assert.Equal(new[] { "pulling" }, running?.Log);
        Assert.Equal("1.0.0.2", running?.Record.From);
        Assert.Equal(UpdateJournal.Failed, journal.Read(new Version(1, 0, 0, 2), started.AddHours(1))?.State);

        File.WriteAllText(journal.ExitOf("1.0.1.0"), "0\n");

        Assert.Equal(UpdateJournal.Done, journal.Read(new Version(1, 0, 1, 0), started.AddMinutes(2))?.State);
        Assert.Equal(UpdateJournal.Failed, journal.Read(new Version(1, 0, 0, 2), started.AddMinutes(2))?.State);
    }

    [Fact]
    public void AnUpdateThatDidNotStartFailsWithItsReason()
    {
        var journal = new UpdateJournal(_folder.FullName);
        var started = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
        journal.Begin(new UpdateRecord("1.0.0.2", "1.0.1.0", UpdateModes.Package, started));
        journal.Refuse("1.0.1.0", "the release carries no package for riscv64");

        var run = journal.Read(new Version(1, 0, 0, 2), started);

        Assert.Equal(UpdateJournal.Failed, run?.State);
        Assert.Contains("the release carries no package for riscv64", run?.Log ?? []);
    }

    [Fact]
    public void AnUpdateThatWentThroughStaysDoneOnceThePanelMovesOn()
    {
        var journal = new UpdateJournal(_folder.FullName);
        var started = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
        journal.Begin(new UpdateRecord("1.0.0.2", "1.0.1.0", UpdateModes.Docker, started));
        File.WriteAllText(journal.ExitOf("1.0.1.0"), "0\n");

        Assert.Equal(UpdateJournal.Done, journal.Read(new Version(1, 0, 1, 1), started.AddDays(2))?.State);
    }

    [Theory]
    [InlineData("1", "1.0.1.1", 30)]
    [InlineData(null, "1.0.1.1", 90)]
    [InlineData(null, "1.0.1.1", 5)]
    [InlineData("0", "1.0.0.1", 30)]
    [InlineData("1", "1.0.0.1", 30)]
    public void TheRecordOfAnUpdateIsLeftOutOnceThePanelRunsNeitherOfItsVersions(string? exit, string current, int minutes)
    {
        var journal = new UpdateJournal(_folder.FullName);
        var started = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
        journal.Begin(new UpdateRecord("1.0.0.2", "1.0.1.0", UpdateModes.Docker, started));
        if (exit is not null)
        {
            File.WriteAllText(journal.ExitOf("1.0.1.0"), exit + "\n");
        }

        Assert.Null(journal.Read(Version.Parse(current), started.AddMinutes(minutes)));
    }

    [Fact]
    public void AnUpdateWhoseToolLeftNoExitCodeIsDoneOnceThePanelRunsItsTarget()
    {
        var journal = new UpdateJournal(_folder.FullName);
        var started = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
        journal.Begin(new UpdateRecord("1.0.0.2", "1.0.1.0", UpdateModes.Package, started));

        Assert.Equal(UpdateJournal.Running, journal.Read(new Version(1, 0, 1, 0), started.AddMinutes(5))?.State);
        Assert.Equal(UpdateJournal.Done, journal.Read(new Version(1, 0, 1, 0), started.AddHours(1))?.State);
    }

    [Theory]
    [InlineData("1", "1.0.0.2")]
    [InlineData("1", "1.0.1.0")]
    [InlineData("0", "1.0.0.2")]
    public void AnUpdateThatFailedStaysFailedWhileThePanelRunsOneOfItsVersions(string exit, string current)
    {
        var journal = new UpdateJournal(_folder.FullName);
        var started = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
        journal.Begin(new UpdateRecord("1.0.0.2", "1.0.1.0", UpdateModes.Docker, started));
        File.WriteAllText(journal.ExitOf("1.0.1.0"), exit + "\n");

        Assert.Equal(UpdateJournal.Failed, journal.Read(Version.Parse(current), started.AddDays(2))?.State);
    }

    [Fact]
    public void TheFailureSeenStaysWrittenDownUntilTheNextUpdate()
    {
        var journal = new UpdateJournal(_folder.FullName);
        var started = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
        journal.Begin(new UpdateRecord("1.0.0.2", "1.0.1.0", UpdateModes.Package, started));
        journal.Refuse("1.0.1.0", "the release carries no package for riscv64");

        var unseen = journal.Read(new Version(1, 0, 0, 2), started);
        journal.See();
        var seen = journal.Read(new Version(1, 0, 0, 2), started);
        journal.Begin(new UpdateRecord("1.0.0.2", "1.0.1.1", UpdateModes.Package, started.AddHours(1)));
        var next = journal.Read(new Version(1, 0, 0, 2), started.AddHours(1));

        Assert.False(unseen?.Record.Seen);
        Assert.Equal(UpdateJournal.Failed, seen?.State);
        Assert.True(seen?.Record.Seen);
        Assert.Contains("the release carries no package for riscv64", seen?.Log ?? []);
        Assert.Equal(UpdateJournal.Running, next?.State);
        Assert.False(next?.Record.Seen);
    }

    [Fact]
    public void ARecordWrittenBeforeTheSeenMarkReadsAsNotSeen()
    {
        File.WriteAllText(
            Path.Combine(_folder.FullName, "pending.json"),
            "{\"from\":\"1.0.0.2\",\"to\":\"1.0.1.0\",\"mode\":\"docker\",\"started\":\"2026-09-21T10:00:00+00:00\"}");
        File.WriteAllText(Path.Combine(_folder.FullName, "1.0.1.0.rc"), "1\n");

        var run = new UpdateJournal(_folder.FullName).Read(new Version(1, 0, 0, 2), DateTimeOffset.UtcNow);

        Assert.Equal(UpdateJournal.Failed, run?.State);
        Assert.False(run?.Record.Seen);
    }

    [Fact]
    public void TheLogsOfAllButTheLastUpdatesGo()
    {
        var journal = new UpdateJournal(_folder.FullName);
        journal.Begin(new UpdateRecord("1.0.1.0", "1.0.2.0", UpdateModes.Docker, DateTimeOffset.UtcNow));
        var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var minor = 1; minor <= 8; minor++)
        {
            var version = $"1.0.{minor}.0";
            File.WriteAllText(journal.LogOf(version), "log\n");
            File.WriteAllText(journal.ExitOf(version), "0\n");
            File.SetLastWriteTimeUtc(journal.LogOf(version), day.AddDays(minor));
        }

        File.WriteAllText(Path.Combine(_folder.FullName, "notes.log"), "kept\n");

        journal.Prune();

        var left = _folder.GetFiles().Select(file => file.Name).Order(StringComparer.Ordinal);
        var kept = new[] { "1.0.2.0", "1.0.4.0", "1.0.5.0", "1.0.6.0", "1.0.7.0", "1.0.8.0" }
            .SelectMany(version => new[] { version + ".log", version + ".rc" })
            .Append("notes.log")
            .Append("pending.json");

        Assert.Equal(kept, left);
    }

    [Fact]
    public void TheEndOfAnUpdateIsSeenOnlyOnceItEnded()
    {
        using var bench = new Bench();
        using var handler = new Files(new());
        var options = new UpdateOptions { Directory = Path.Combine(_folder.FullName, "update") };
        var center = new UpdateCenter(options, new Clients(handler), bench.Clock, NullLogger<UpdateCenter>.Instance, bench.Scopes);
        var journal = new UpdateJournal(options.Directory);
        var current = UpdateCenter.Current;
        var next = new Version(current.Major, current.Minor, current.Build + 1, 0).ToString();
        journal.Begin(new UpdateRecord(current.ToString(), next, UpdateModes.Package, bench.Clock.GetUtcNow()));

        var running = center.See();
        journal.Refuse(next, "the release carries no package for riscv64");
        var ended = center.See();

        Assert.Equal("update-busy", running?.Error);
        Assert.Null(ended);
        Assert.Equal(UpdateJournal.Failed, center.Status().Run?.State);
        Assert.True(center.Status().Run?.Seen);
    }

    [Fact]
    public void NoUpdateLeavesNoRun()
    {
        Assert.Null(new UpdateJournal(_folder.FullName).Read(new Version(1, 0, 0, 2), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void TheWayThePanelRunsIsTold()
    {
        var release = new HashSet<string> { "/opt/amneziageo-server/current/install.sh", "/opt/amneziageo-server/current/release" };

        Assert.Equal(UpdateModes.Docker, UpdateModes.Detect("/opt/amneziageo-server/", release.Contains, "1"));
        Assert.Equal(UpdateModes.Docker, UpdateModes.Detect("/opt/amneziageo-server/", path => path == "/.dockerenv", null));
        Assert.Equal(UpdateModes.Package, UpdateModes.Detect("/opt/amneziageo-server/current/", release.Contains, null));
        Assert.Equal(UpdateModes.Manual, UpdateModes.Detect("/home/bor/dev/bin/", release.Contains, null));
    }

    [Fact]
    public void TheContainerIsFoundByItsMounts()
    {
        var id = new string('a', 64);
        var mountinfo = $"812 790 8:2 /var/lib/docker/containers/{id}/resolv.conf /etc/resolv.conf rw,relatime - ext4 /dev/sda2 rw\n";

        Assert.Equal(id, DockerUpdater.ContainerOf(mountinfo));
        Assert.Null(DockerUpdater.ContainerOf("25 1 8:2 / / rw,relatime - ext4 /dev/sda2 rw\n"));
    }

    [Fact]
    public void ThePlaceOfThePanelIsReadOutOfItsContainer()
    {
        var (place, blocker) = DockerUpdater.Place(Inspect(), Data, Socket);

        Assert.Equal(string.Empty, blocker);
        Assert.NotNull(place);
        Assert.Equal("amneziageo-server", place.Project);
        Assert.Equal("/opt/amneziageo-docker", place.Folder);
        Assert.Equal("panel", place.Service);
        Assert.Equal("amneziageo-server:5bd40f0-0921", place.Image);
        Assert.Equal(Data, place.Data);
        Assert.Equal("/run/docker.sock", place.Socket);
        Assert.Equal("amneziageo-server_settings", DockerUpdater.Place(Inspect(), "/etc/amneziageo-server", Socket).Place?.Data);
    }

    [Fact]
    public void APanelOutOfComposeOrWithoutItsMountsCannotMoveItself()
    {
        var bare = Inspect();
        bare["Config"]!["Labels"] = new JsonObject();
        var elsewhere = Inspect();
        elsewhere["Config"]!["Labels"]!["com.docker.compose.project.config_files"] = "/root/compose.yaml";

        Assert.Equal("no-compose", DockerUpdater.Place(bare, Data, Socket).Blocker);
        Assert.Equal("compose-elsewhere", DockerUpdater.Place(elsewhere, Data, Socket).Blocker);
        Assert.Equal("no-data", DockerUpdater.Place(Inspect(), "/data", Socket).Blocker);
        Assert.Equal("no-docker", DockerUpdater.Place(Inspect(), Data, "/docker.sock").Blocker);
    }

    [Theory]
    [InlineData("amneziageo-server:5bd40f0-0921", "amneziageo-server")]
    [InlineData("localhost:5000/team/panel:1.0.1.0", "localhost:5000/team/panel")]
    [InlineData("ghcr.io/bor-project/amneziageo-server@sha256:" + Digest, "ghcr.io/bor-project/amneziageo-server")]
    [InlineData("amneziageo-server", "amneziageo-server")]
    public void TheRepositoryOfAnImageLeavesItsTagAndDigestOut(string image, string repository)
    {
        Assert.Equal(repository, DockerUpdater.Repository(image));
    }

    [Fact]
    public void TheToolIsGivenTheProjectTheDataAndTheSocket()
    {
        var (place, _) = DockerUpdater.Place(Inspect(), Data, Socket);
        var handover = new DockerHandover("amneziageo-server:1.0.1.0", "1.0.0.2", "1.0.1.0", Data, Data + "/update");

        var spec = DockerUpdater.Spec(place!, handover);

        Assert.Equal("amneziageo-server:1.0.1.0", spec["Image"]!.GetValue<string>());
        Assert.Equal(
            new[] { "/run/docker.sock:/var/run/docker.sock", "/opt/amneziageo-docker:/opt/amneziageo-docker", Data + ":" + Data },
            Words(spec["HostConfig"]!["Binds"]));
        Assert.Contains("AMNEZIAGEO_UPDATE_TAG=1.0.1.0", Words(spec["Env"]));
        Assert.Contains("AMNEZIAGEO_UPDATE_CONTAINER=" + place!.Container, Words(spec["Env"]));
        Assert.Contains("AMNEZIAGEO_UPDATE_FILES=/opt/amneziageo-docker/compose.yaml", Words(spec["Env"]));
        Assert.Contains("AMNEZIAGEO_UPDATE_WORK=" + Data + "/update", Words(spec["Env"]));
        Assert.Equal(new[] { DockerUpdater.Tool }, Words(spec["Entrypoint"]));
        Assert.Equal("1.0.1.0", spec["Labels"]![DockerUpdater.Mark]!.GetValue<string>());
    }

    private static byte[] Manifest(string version, string channel = "stable") =>
        Encoding.UTF8.GetBytes(new JsonObject
        {
            ["version"] = version,
            ["channel"] = channel,
            ["published"] = "2026-09-21T10:00:00Z",
            ["commit"] = "c7c0b32",
            ["image"] = "ghcr.io/bor-project/amneziageo-server@sha256:" + Digest,
            ["packages"] = new JsonArray(Pack(version, "x64"), Pack(version, "arm64")),
        }.ToJsonString());

    private static JsonObject Pack(string version, string arch) => new()
    {
        ["name"] = $"amneziageo-server-{version}-linux-{arch}.tar.gz",
        ["arch"] = arch,
        ["size"] = 1000,
        ["sha256"] = Digest,
    };

    private static byte[] Sign(ECDsa key, byte[] data) =>
        key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    private static Dictionary<string, byte[]> Published(byte[] manifest, byte[] signature) => new()
    {
        ["https://releases.test/1.0.1.0/update.json"] = manifest,
        ["https://releases.test/1.0.1.0/update.json.sig"] = signature,
    };

    private static byte[] Releases() =>
        Encoding.UTF8.GetBytes(new JsonArray(
            Release("v1.0.2.0", draft: true, prerelease: false, UpdateFeed.ManifestFile, UpdateFeed.SignatureFile),
            Release("v1.0.1.3", draft: false, prerelease: true, UpdateFeed.ManifestFile, UpdateFeed.SignatureFile),
            Release("v1.0.1.5", draft: false, prerelease: false, UpdateFeed.ManifestFile),
            Release("v1.0.1.0", draft: false, prerelease: false, UpdateFeed.ManifestFile, UpdateFeed.SignatureFile),
            Release("v1.0.0.9", draft: false, prerelease: false, UpdateFeed.ManifestFile, UpdateFeed.SignatureFile)).ToJsonString());

    private static JsonObject Release(string tag, bool draft, bool prerelease, params string[] assets)
    {
        var list = new JsonArray();
        foreach (var name in assets)
        {
            list.Add(new JsonObject
            {
                ["name"] = name,
                ["browser_download_url"] = $"https://github.com/bor-project/amneziageo_server/releases/download/{tag}/{name}",
            });
        }

        return new JsonObject
        {
            ["tag_name"] = tag,
            ["draft"] = draft,
            ["prerelease"] = prerelease,
            ["html_url"] = $"https://github.com/bor-project/amneziageo_server/releases/tag/{tag}",
            ["assets"] = list,
        };
    }

    private static UpdateOffer Offer(byte[] archive, string sha) =>
        new(
            new UpdateManifest(
                new Version(1, 0, 1, 0),
                UpdateOptions.Stable,
                null,
                string.Empty,
                string.Empty,
                [new UpdatePackage("amneziageo-server-1.0.1.0-linux-x64.tar.gz", "x64", archive.Length, sha)]),
            new Uri("https://releases.test/r/"),
            string.Empty);

    private static byte[] Package()
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "amneziageo-server/install.sh")
            {
                DataStream = new MemoryStream(Encoding.ASCII.GetBytes("#!/bin/sh\n")),
            });
        }

        return buffer.ToArray();
    }

    private static JsonNode Inspect() => new JsonObject
    {
        ["Id"] = new string('b', 64),
        ["Config"] = new JsonObject
        {
            ["Image"] = "amneziageo-server:5bd40f0-0921",
            ["Labels"] = new JsonObject
            {
                ["com.docker.compose.project"] = "amneziageo-server",
                ["com.docker.compose.project.working_dir"] = "/opt/amneziageo-docker",
                ["com.docker.compose.project.config_files"] = "/opt/amneziageo-docker/compose.yaml",
                ["com.docker.compose.service"] = "panel",
            },
        },
        ["Mounts"] = new JsonArray(
            new JsonObject { ["Type"] = "bind", ["Source"] = Data, ["Destination"] = Data },
            new JsonObject
            {
                ["Type"] = "volume",
                ["Name"] = "amneziageo-server_settings",
                ["Source"] = "/var/lib/docker/volumes/amneziageo-server_settings/_data",
                ["Destination"] = "/etc/amneziageo-server",
            },
            new JsonObject { ["Type"] = "bind", ["Source"] = "/run/docker.sock", ["Destination"] = Socket }),
    };

    private static string[] Words(JsonNode? list) =>
        [.. list!.AsArray().Select(one => one!.GetValue<string>())];

    private sealed class Files : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files;

        public Files(Dictionary<string, byte[]> files)
        {
            _files = files;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_files.TryGetValue(request.RequestUri!.AbsoluteUri, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static readonly UpdatePatience Quick =
        new(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(10), 3);

    private static byte[] One(string tag, bool early) =>
        Encoding.UTF8.GetBytes(
            Release(tag, draft: false, prerelease: early, UpdateFeed.ManifestFile, UpdateFeed.SignatureFile).ToJsonString());

    private static HttpResponseMessage Plain(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Packed(byte[] body)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(body);
        }

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentEncoding.Add("gzip");

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    // GitHub as the look for a release sees it: the answer about releases is scripted by the count of the asking, a
    // null answer never comes; the manifests of 1.0.1.0 and 1.0.1.3 lie where a release keeps them.
    private sealed class GitHub : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = [];

        private readonly Func<int, HttpRequestMessage, HttpResponseMessage?> _releases;

        public GitHub(ECDsa key, Func<int, HttpRequestMessage, HttpResponseMessage?> releases)
        {
            _releases = releases;
            foreach (var (tag, version, channel) in new[] { ("v1.0.1.0", "1.0.1.0", "stable"), ("v1.0.1.3", "1.0.1.3", "test") })
            {
                var manifest = Manifest(version, channel);
                var folder = $"https://github.com/bor-project/amneziageo_server/releases/download/{tag}/";
                _files[folder + UpdateFeed.ManifestFile] = manifest;
                _files[folder + UpdateFeed.SignatureFile] = Sign(key, manifest);
            }
        }

        public int Asked { get; private set; }

        public string Address { get; private set; } = string.Empty;

        public string Packing { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var address = request.RequestUri!;
            if (address.Host != "api.github.com")
            {
                return _files.TryGetValue(address.AbsoluteUri, out var body)
                    ? Plain(body)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            Asked++;
            Address = address.AbsoluteUri;
            Packing = request.Headers.AcceptEncoding.ToString();
            var answer = _releases(Asked, request);
            if (answer is null)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return answer!;
        }
    }

    private sealed class Clients : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public Clients(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
