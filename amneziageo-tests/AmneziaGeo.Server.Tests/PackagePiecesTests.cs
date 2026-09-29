using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Api.Updates;

namespace AmneziaGeo.Server.Tests;

public sealed class PackagePiecesTests : IDisposable
{
    private const string Files = "https://releases.test/r/";

    private const string Name = "amneziageo-server-1.0.1.0-linux-x64";

    private const UnixFileMode Plain = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead
        | UnixFileMode.OtherRead;

    private const UnixFileMode Runnable = Plain | UnixFileMode.UserExecute | UnixFileMode.GroupExecute
        | UnixFileMode.OtherExecute;

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-pieces-");

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void AListOfFilesReadsWhole()
    {
        var release = Build(Package());

        var entries = PackageList.Parse(release.List, release.Pack.Length);

        Assert.Equal(7, entries.Count);
        Assert.Equal("amneziageo-server/install.sh", entries[0].Path);
        Assert.Equal(Runnable, entries[0].Mode);
        Assert.Equal(0, entries[0].Offset);
        Assert.Equal(entries[0].Length, entries[1].Offset);
        Assert.Equal(release.Pack.Length, entries[^1].Offset + entries[^1].Length);
        Assert.Equal(Hash(Encoding.ASCII.GetBytes("20260929-1\n")), entries[^1].Sha256);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("outside")]
    [InlineData("rooted")]
    [InlineData("empty")]
    [InlineData("gap")]
    [InlineData("short")]
    [InlineData("twice")]
    [InlineData("mode")]
    [InlineData("digest")]
    public void AListThatDoesNotHoldTogetherIsRefused(string flaw)
    {
        var sha = new string('a', 64);
        var first = $"{sha} 755 10 0 20 amneziageo-server/install.sh";
        var second = $"{sha} 644 10 20 30 amneziageo-server/release";
        var lines = flaw switch
        {
            "head" => new[] { "# amneziageo-server files 2", first, second },
            "outside" => new[] { PackageList.Head, first, second.Replace("server/release", "server/../release", StringComparison.Ordinal) },
            "rooted" => new[] { PackageList.Head, first, second.Replace("amneziageo-server/release", "/etc/release", StringComparison.Ordinal) },
            "empty" => new[] { PackageList.Head, first, second.Replace("server/release", "server//release", StringComparison.Ordinal) },
            "gap" => new[] { PackageList.Head, first, second.Replace(" 20 30 ", " 21 29 ", StringComparison.Ordinal) },
            "short" => new[] { PackageList.Head, first },
            "twice" => new[] { PackageList.Head, first, second.Replace("release", "install.sh", StringComparison.Ordinal) },
            "mode" => new[] { PackageList.Head, first.Replace(" 755 ", " 955 ", StringComparison.Ordinal), second },
            _ => new[] { PackageList.Head, first.Replace(sha, sha.ToUpperInvariant(), StringComparison.Ordinal), second },
        };

        var text = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");

        Assert.Throws<InvalidDataException>(() => PackageList.Parse(text, 50));
    }

    [Fact]
    public void AManifestReadsTheListAndThePackOfAPackage()
    {
        var sha = new string('b', 64);
        var json = Encoding.UTF8.GetBytes(
            $$"""
            {"version": "1.0.1.0", "packages": [
              {"name": "{{Name}}.tar.gz", "arch": "x64", "size": 1000, "sha256": "{{sha}}",
               "files": {"name": "{{Name}}.files", "size": 40, "sha256": "{{sha}}"}, "pack": {"name": "{{Name}}.pack", "size": 900} },
              {"name": "amneziageo-server-1.0.1.0-linux-arm64.tar.gz", "arch": "arm64", "size": 1000, "sha256": "{{sha}}",
               "files": {"name": "../list.files", "size": 40, "sha256": "{{sha}}"}, "pack": {"name": "arm64.pack", "size": 0} }]}
            """);

        var manifest = UpdateManifest.Parse(json);

        Assert.Equal(new UpdateFile(Name + ".files", 40, sha), manifest.PackageFor("x64")?.Files);
        Assert.Equal(new UpdateFile(Name + ".pack", 900, string.Empty), manifest.PackageFor("x64")?.Pack);
        Assert.Null(manifest.PackageFor("arm64")?.Files);
        Assert.Null(manifest.PackageFor("arm64")?.Pack);
    }

    [Fact]
    public async Task AnUpdateFetchesOnlyTheFilesTheHostLacks()
    {
        var files = Package();
        var release = Build(files);
        var held = Hold(files, "install.sh", "publish/other.dll", "publish/runtime.dll", "publish/wwwroot/index.html");
        using var feed = new Feed(release);
        using var http = new HttpClient(feed);
        var notes = new List<string>();

        var installer = await new PackageUpdater(http)
            .StageAsync(Offer(release), "x64", held, _folder.FullName, notes.Add, CancellationToken.None);

        AssertStaged(files, installer);
        Assert.DoesNotContain(feed.Asked, asked => asked.StartsWith(Name + ".tar.gz", StringComparison.Ordinal));
        Assert.Equal([Name + ".files", Name + ".pack " + Part(release, "publish/panel.dll", "publish/panel.dll"),
            Name + ".pack " + Part(release, "publish/wwwroot/app.js", "release")], feed.Asked);
        Assert.StartsWith("fetched 3 of the 7 files", Assert.Single(notes), StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            var top = Path.GetDirectoryName(installer)!;
            foreach (var folder in Directory.GetDirectories(top, "*", SearchOption.AllDirectories).Append(top))
            {
                Assert.Equal(Runnable, File.GetUnixFileMode(folder));
            }
        }
    }

    [Fact]
    public async Task AFeedThatDoesNotAnswerInPartsGivesTheWholePackage()
    {
        var files = Package();
        var release = Build(files);
        var held = Hold(files, "install.sh", "publish/other.dll", "publish/runtime.dll");
        using var feed = new Feed(release) { Whole = true };
        using var http = new HttpClient(feed);
        var notes = new List<string>();

        var installer = await new PackageUpdater(http)
            .StageAsync(Offer(release), "x64", held, _folder.FullName, notes.Add, CancellationToken.None);

        AssertStaged(files, installer);
        Assert.Equal(Name + ".tar.gz", feed.Asked[^1]);
        Assert.Contains("answered 200 to a request for a part", Assert.Single(notes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APieceThatDoesNotMatchTheListGivesTheWholePackage()
    {
        var files = Package();
        var release = Build(files);
        var entry = PackageList.Parse(release.List, release.Pack.Length).Single(one => one.Path.EndsWith("panel.dll", StringComparison.Ordinal));
        release.Pack[entry.Offset + (entry.Length / 2)] ^= 0xff;
        var held = Hold(files, "install.sh", "publish/other.dll", "publish/runtime.dll");
        using var feed = new Feed(release);
        using var http = new HttpClient(feed);
        var notes = new List<string>();

        var installer = await new PackageUpdater(http)
            .StageAsync(Offer(release), "x64", held, _folder.FullName, notes.Add, CancellationToken.None);

        AssertStaged(files, installer);
        Assert.Equal(Name + ".tar.gz", feed.Asked[^1]);
        Assert.Contains("the whole package is downloaded", Assert.Single(notes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AListThatDoesNotMatchTheManifestGivesTheWholePackage()
    {
        var files = Package();
        var release = Build(files);
        var held = Hold(files, "install.sh", "publish/other.dll", "publish/runtime.dll");
        using var feed = new Feed(release);
        using var http = new HttpClient(feed);
        var notes = new List<string>();
        var offer = Offer(release, list: new UpdateFile(Name + ".files", release.List.Length, new string('c', 64)));

        var installer = await new PackageUpdater(http)
            .StageAsync(offer, "x64", held, _folder.FullName, notes.Add, CancellationToken.None);

        AssertStaged(files, installer);
        Assert.Equal([Name + ".files", Name + ".tar.gz"], feed.Asked);
        Assert.Contains("does not match the digest the manifest names", Assert.Single(notes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHostThatHoldsLittleOfThePackageGetsItWhole()
    {
        var files = Package();
        var release = Build(files);
        var held = Hold(files);
        using var feed = new Feed(release);
        using var http = new HttpClient(feed);
        var notes = new List<string>();

        var installer = await new PackageUpdater(http)
            .StageAsync(Offer(release), "x64", held, _folder.FullName, notes.Add, CancellationToken.None);

        AssertStaged(files, installer);
        Assert.Equal([Name + ".files", Name + ".tar.gz"], feed.Asked);
        Assert.Contains("most of the package changed", Assert.Single(notes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleaseWithoutAListGivesTheWholePackageSilently()
    {
        var files = Package();
        var release = Build(files);
        var held = Hold(files, "install.sh", "publish/other.dll", "publish/runtime.dll");
        using var feed = new Feed(release);
        using var http = new HttpClient(feed);
        var notes = new List<string>();
        var offer = Offer(release) with { Manifest = Offer(release).Manifest with { Packages = [Whole(release)] } };

        var installer = await new PackageUpdater(http)
            .StageAsync(offer, "x64", held, _folder.FullName, notes.Add, CancellationToken.None);

        AssertStaged(files, installer);
        Assert.Equal([Name + ".tar.gz"], feed.Asked);
        Assert.Empty(notes);
    }

    private static List<(string Path, byte[] Data, UnixFileMode Mode)> Package() =>
    [
        ("amneziageo-server/install.sh", Encoding.ASCII.GetBytes("#!/bin/sh\necho install\n"), Runnable),
        ("amneziageo-server/publish/other.dll", Noise(1, 200 * 1024), Plain),
        ("amneziageo-server/publish/panel.dll", Noise(2, 50 * 1024), Plain),
        ("amneziageo-server/publish/runtime.dll", Noise(3, 300 * 1024), Plain),
        ("amneziageo-server/publish/wwwroot/app.js", Noise(4, 20 * 1024), Plain),
        ("amneziageo-server/publish/wwwroot/index.html", Encoding.ASCII.GetBytes("<!doctype html>\n"), Plain),
        ("amneziageo-server/release", Encoding.ASCII.GetBytes("20260929-1\n"), Plain),
    ];

    private static byte[] Noise(int seed, int size)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);

        return data;
    }

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static Built Build(List<(string Path, byte[] Data, UnixFileMode Mode)> files)
    {
        using var pack = new MemoryStream();
        var list = new StringBuilder(PackageList.Head + "\n");
        foreach (var (path, data, mode) in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            using var piece = new MemoryStream();
            using (var gzip = new GZipStream(piece, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(data);
            }

            list.Append(Hash(data)).Append(' ').Append(Convert.ToString((int)mode, 8)).Append(' ')
                .Append(data.Length).Append(' ').Append(pack.Length).Append(' ').Append(piece.Length).Append(' ')
                .Append(path).Append('\n');
            piece.WriteTo(pack);
        }

        using var archive = new MemoryStream();
        using (var gzip = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            foreach (var (path, data, mode) in files)
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path) { Mode = mode, DataStream = new MemoryStream(data) });
            }
        }

        return new Built(archive.ToArray(), pack.ToArray(), Encoding.UTF8.GetBytes(list.ToString()));
    }

    private static UpdatePackage Whole(Built release) =>
        new(Name + ".tar.gz", "x64", release.Archive.Length, Hash(release.Archive));

    private static UpdateOffer Offer(Built release, UpdateFile? list = null) =>
        new(
            new UpdateManifest(
                new Version(1, 0, 1, 0),
                UpdateOptions.Stable,
                null,
                string.Empty,
                string.Empty,
                [Whole(release) with
                {
                    Files = list ?? new UpdateFile(Name + ".files", release.List.Length, Hash(release.List)),
                    Pack = new UpdateFile(Name + ".pack", release.Pack.Length, string.Empty),
                }]),
            new Uri(Files),
            string.Empty);

    private static string Part(Built release, string first, string last)
    {
        var entries = PackageList.Parse(release.List, release.Pack.Length);
        var from = entries.Single(entry => entry.Path == "amneziageo-server/" + first);
        var to = entries.Single(entry => entry.Path == "amneziageo-server/" + last);

        return $"bytes={from.Offset}-{to.Offset + to.Length - 1}";
    }

    private List<string> Hold(List<(string Path, byte[] Data, UnixFileMode Mode)> files, params string[] kept)
    {
        var release = Directory.CreateDirectory(Path.Combine(_folder.FullName, "held", "release")).FullName;
        var web = Directory.CreateDirectory(Path.Combine(_folder.FullName, "held", "web")).FullName;
        File.WriteAllBytes(Path.Combine(release, "panel.dll"), Noise(9, 50 * 1024));
        foreach (var (path, data, _) in files.Where(file => kept.Contains(file.Path["amneziageo-server/".Length..])))
        {
            var name = path["amneziageo-server/".Length..];
            var place = name.StartsWith("publish/wwwroot/", StringComparison.Ordinal)
                ? Path.Combine(web, name["publish/wwwroot/".Length..])
                : Path.Combine(release, Path.GetFileName(name));
            File.WriteAllBytes(place, data);
        }

        return [release, web];
    }

    private void AssertStaged(List<(string Path, byte[] Data, UnixFileMode Mode)> files, string installer)
    {
        var package = Path.Combine(_folder.FullName, "stage-1.0.1.0", "package");
        Assert.Equal(Path.Combine(package, "amneziageo-server", "install.sh"), installer);
        Assert.Equal(files.Count, Directory.GetFiles(package, "*", SearchOption.AllDirectories).Length);
        foreach (var (path, data, mode) in files)
        {
            var staged = Path.Combine(package, path);
            Assert.Equal(data, File.ReadAllBytes(staged));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(mode, File.GetUnixFileMode(staged));
            }
        }
    }

    private sealed record Built(byte[] Archive, byte[] Pack, byte[] List);

    private sealed class Feed : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files;

        public Feed(Built release)
        {
            _files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [Name + ".tar.gz"] = release.Archive,
                [Name + ".files"] = release.List,
                [Name + ".pack"] = release.Pack,
            };
        }

        public bool Whole { get; init; }

        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = request.RequestUri!.AbsoluteUri[Files.Length..];
            var range = request.Headers.Range?.Ranges.Single();
            Asked.Add(range is null ? name : $"{name} bytes={range.From}-{range.To}");
            if (!_files.TryGetValue(name, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (range is null || Whole)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
            }

            var from = range.From!.Value;
            var to = Math.Min(range.To!.Value, body.Length - 1);
            var content = new ByteArrayContent(body, (int)from, (int)(to - from + 1));
            content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, body.Length);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        }
    }
}
