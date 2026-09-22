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
    public async Task APackageIsDownloadedCheckedAndUnpacked()
    {
        var archive = Package();
        var offer = Offer(archive, Convert.ToHexStringLower(SHA256.HashData(archive)));
        using var http = new HttpClient(new Files(new Dictionary<string, byte[]>
        {
            ["https://releases.test/r/amneziageo-server-1.0.1.0-linux-x64.tar.gz"] = archive,
        }));

        var installer = await new PackageUpdater(http).StageAsync(offer, "x64", _folder.FullName, CancellationToken.None);

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
            new PackageUpdater(http).StageAsync(Offer(archive, Digest), "x64", _folder.FullName, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PackageUpdater(http).StageAsync(Offer(archive, Digest), "riscv64", _folder.FullName, CancellationToken.None));
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
