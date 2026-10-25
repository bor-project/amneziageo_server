using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AmneziaGeo.Server.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public sealed class ClientReleaseTests : IDisposable
{
    private const ushort X64 = 0x8664;

    private const ushort Arm64 = 0xAA64;

    private static readonly Version Nine = new(1, 10, 9, 0);

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-releases-");

    public void Dispose() => _folder.Delete(recursive: true);

    [Theory]
    [InlineData("v1.10.9.0", "stable", 0)]
    [InlineData("v1.10.15.0-beta.1", "beta", 1)]
    [InlineData("v1.10.16.0-test.12", "test", 12)]
    public void ATagReadsItsVersionChannelAndNumber(string text, string channel, int number)
    {
        Assert.True(ReleaseTag.TryParse(text, out var tag));
        Assert.Equal(channel, tag.Channel);
        Assert.Equal(number, tag.Number);
        Assert.Equal(4, tag.Version.Revision >= 0 ? 4 : 0);
        Assert.Equal(text, tag.ToString());
    }

    [Theory]
    [InlineData("1.10.9.0")]
    [InlineData("v1.10.9")]
    [InlineData("v1.10.9.0-stable.1")]
    [InlineData("v1.10.9.0-beta.0")]
    [InlineData("v1.10.9.0-rc.1")]
    [InlineData("v1.10.9.0-beta")]
    public void ANameThatIsNotATagIsRefused(string text) => Assert.False(ReleaseTag.TryParse(text, out _));

    [Fact]
    public void TagsOrderByVersionThenStableOverBetaOverTest()
    {
        var names = new[] { "v1.10.9.0-test.2", "v1.10.10.0-beta.1", "v1.10.9.0", "v1.10.9.0-beta.3", "v1.10.9.0-beta.1", "v1.10.8.0" };
        var tags = names.Select(name => ReleaseTag.TryParse(name, out var tag) ? tag : null!).ToList();

        tags.Sort();

        Assert.Equal(
            ["v1.10.8.0", "v1.10.9.0-test.2", "v1.10.9.0-beta.1", "v1.10.9.0-beta.3", "v1.10.9.0", "v1.10.10.0-beta.1"],
            tags.Select(tag => tag.ToString()));
    }

    [Fact]
    public void AReleaseCarriesTwentyFiles()
    {
        var slots = ReleaseSlots.Of(Nine);

        Assert.Equal(ReleaseSlots.Count, slots.Count);
        Assert.Equal(ReleaseSlots.Count, slots.Select(slot => slot.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(slots, slot => slot is { Name: "AmneziaGeo-1.10.9.0-win-x64-fdd.exe", Platform: "windows", Arch: "x64", Variant: "fdd", Installer: null });
        Assert.Contains(slots, slot => slot is { Name: "amneziageo-gui_1.10.9.0_arm64.deb", Platform: "linux", Arch: "arm64", Variant: "amneziageo-gui" });
        Assert.Contains(slots, slot => slot is { Name: "AmneziaGeo-1.10.9.0-android.apk", Platform: "android", Arch: "universal", Variant: "apk" });
        Assert.Equal("amneziageo_1.10.9.0_amd64.deb", ReleaseSlots.Find(Nine, "amneziageo_1.10.9.0_amd64.patch")?.Installer);
        Assert.Null(ReleaseSlots.Find(Nine, "AmneziaGeo-1.10.9.0-android.patch"));
        Assert.Null(ReleaseSlots.Find(Nine, "AmneziaGeo-1.10.8.0-win-x64.exe"));
        Assert.Equal(12, slots.Count(slot => !slot.IsPatch));
    }

    [Fact]
    public void TheManifestNamesInstallersWithTheirPatchesAndTheSetup()
    {
        var tag = new ReleaseTag(Nine, ReleaseChannel.Beta, 2);
        var files = new List<ReleaseFileInfo>
        {
            new("AmneziaGeo-1.10.9.0-win-x64.exe", 10, new string('a', 64), string.Empty),
            new("AmneziaGeo-1.10.9.0-win-x64.patch", 3, new string('b', 64), "1.10.4.0"),
            new("AmneziaGeo-1.10.9.0-win-arm64.patch", 3, new string('c', 64), string.Empty),
            new("AmneziaGeo-1.10.9.0-android-arm64.apk", 7, new string('d', 64), string.Empty),
        };

        var manifest = ReleaseManifest.Build(tag, "beta two", files);
        var again = ReleaseManifest.Parse(manifest.Serialize());

        Assert.Equal("1.10.9.0", manifest.Version);
        Assert.Equal("beta", manifest.Channel);
        Assert.Null(manifest.Setup);
        Assert.Equal(["AmneziaGeo-1.10.9.0-win-x64.exe", "AmneziaGeo-1.10.9.0-android-arm64.apk"], manifest.Installers.Select(one => one.Name));
        Assert.Equal("1.10.4.0", manifest.Installers[0].Patch?.From);
        Assert.Null(manifest.Installers[1].Patch);
        Assert.NotNull(again);
        Assert.Equal(manifest, again with { Installers = manifest.Installers });
        Assert.Equal(2, again.Installers.Count);
        Assert.Equal(
            "AmneziaGeo-1.10.9.0-win-x64-fdd.exe",
            ReleaseManifest.Build(tag, string.Empty, [.. files, new("AmneziaGeo-1.10.9.0-win-x64-fdd.exe", 5, new string('e', 64), string.Empty)]).Setup);
    }

    [Fact]
    public void AnExecutableIsMatchedToItsMachine()
    {
        var x64 = Slot("AmneziaGeo-1.10.9.0-win-x64.exe");
        var arm64 = Slot("AmneziaGeo-1.10.9.0-win-arm64-fdd.exe");

        Assert.Null(FileInspector.Check(x64, Written("a.exe", Executable(X64))));
        Assert.Null(FileInspector.Check(arm64, Written("b.exe", Executable(Arm64))));
        Assert.Contains("built for x64, not arm64", FileInspector.Check(arm64, Written("c.exe", Executable(X64)))?.Message, StringComparison.Ordinal);
        Assert.Contains("not a Windows executable", FileInspector.Check(x64, Written("d.exe", "hello"u8.ToArray()))?.Message, StringComparison.Ordinal);
        Assert.Equal(FileFault.Format, FileInspector.Check(x64, Written("e.exe", "hello"u8.ToArray()))?.Reason);
        Assert.Equal("exe", FileInspector.Check(x64, Written("f.exe", "hello"u8.ToArray()))?.Wanted);
    }

    [Fact]
    public void APackageIsMatchedByItsControl()
    {
        var agent = Slot("amneziageo_1.10.9.0_amd64.deb");
        var gui = Slot("amneziageo-gui_1.10.9.0_amd64.deb");
        var arm = Slot("amneziageo_1.10.9.0_arm64.deb");
        var package = Written("a.deb", Package("amneziageo", "amd64"));

        Assert.Null(FileInspector.Check(agent, package));
        Assert.Contains("is amneziageo, not amneziageo-gui", FileInspector.Check(gui, package)?.Message, StringComparison.Ordinal);
        Assert.Contains("built for amd64, not arm64", FileInspector.Check(arm, package)?.Message, StringComparison.Ordinal);
        Assert.Contains("not a Debian package", FileInspector.Check(agent, Written("b.deb", Executable(X64)))?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAndroidPackageIsMatchedByItsLibraries()
    {
        var universal = Slot("AmneziaGeo-1.10.9.0-android.apk");
        var arm64 = Slot("AmneziaGeo-1.10.9.0-android-arm64.apk");
        var whole = Written("a.apk", Apk("arm64-v8a", "armeabi-v7a", "x86_64"));
        var one = Written("b.apk", Apk("arm64-v8a"));

        Assert.Null(FileInspector.Check(universal, whole));
        Assert.Null(FileInspector.Check(arm64, one));
        Assert.Contains("not arm64-v8a", FileInspector.Check(arm64, whole)?.Message, StringComparison.Ordinal);
        Assert.Contains("not arm64-v8a, armeabi-v7a, x86_64", FileInspector.Check(universal, one)?.Message, StringComparison.Ordinal);
        Assert.Contains("not an Android package", FileInspector.Check(arm64, Written("c.apk", Zip(("lib/arm64-v8a/a.so", [1]))))?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APatchIsKnownByItsList()
    {
        var slot = Slot("AmneziaGeo-1.10.9.0-win-x64.patch");

        Assert.Null(FileInspector.Check(slot, Written("a.patch", Patch())));
        Assert.Contains("not a patch", FileInspector.Check(slot, Written("b.patch", Zip(("list", "# something else\n"u8.ToArray()))))?.Message, StringComparison.Ordinal);
        Assert.Contains("not a patch", FileInspector.Check(slot, Written("c.patch", Zip(("files/a", [1]))))?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStableReleaseIsMadeOnceAndBetasAreNumbered()
    {
        var store = new ReleaseStore(_folder.FullName);

        var stable = await store.CreateAsync("1.10.9.0", "stable", " first ", CancellationToken.None);
        var again = await Assert.ThrowsAsync<ReleaseRefused>(() => store.CreateAsync("1.10.9.0", "stable", null, CancellationToken.None));
        var beta = await store.CreateAsync("1.10.10.0", "beta", null, CancellationToken.None);
        var second = await store.CreateAsync("1.10.10.0", "beta", null, CancellationToken.None);
        var test = await store.CreateAsync("1.10.10.0", "test", null, CancellationToken.None);
        var badVersion = await Assert.ThrowsAsync<ReleaseRefused>(() => store.CreateAsync("1.10.9", "stable", null, CancellationToken.None));
        var badChannel = await Assert.ThrowsAsync<ReleaseRefused>(() => store.CreateAsync("1.10.9.0", "rc", null, CancellationToken.None));
        var page = store.List(1, 10);

        Assert.Equal("v1.10.9.0", stable.Release.Tag);
        Assert.Equal("first", stable.Release.Description);
        Assert.Equal(ReleaseSlots.Count, stable.Missing.Count);
        Assert.Equal("release-exists", again.Code);
        Assert.Equal(409, again.Status);
        Assert.Equal("v1.10.10.0-beta.1", beta.Release.Tag);
        Assert.Equal("v1.10.10.0-beta.2", second.Release.Tag);
        Assert.Equal("v1.10.10.0-test.1", test.Release.Tag);
        Assert.Equal("bad-version", badVersion.Code);
        Assert.Equal("bad-channel", badChannel.Code);
        Assert.Equal(4, page.Total);
        Assert.Equal(["v1.10.10.0-beta.2", "v1.10.10.0-beta.1", "v1.10.10.0-test.1", "v1.10.9.0"], page.Items.Select(item => item.Tag));
        Assert.Single(store.List(2, 3).Items);
        Assert.True(File.Exists(Path.Combine(_folder.FullName, "releases", "v1.10.9.0", "update.json")));
        Assert.True(store.Status().Writable);
        Assert.Equal(4, store.Status().Releases);
    }

    [Fact]
    public async Task AFileGoesInWhenItIsWhatItsNameSays()
    {
        var store = new ReleaseStore(_folder.FullName);
        await store.CreateAsync("1.10.9.0", "stable", "nine", CancellationToken.None);

        var taken = await store.PutFileAsync("v1.10.9.0", "AmneziaGeo-1.10.9.0-win-x64.exe", Body(Executable(X64)), null, CancellationToken.None);
        var wrong = await Assert.ThrowsAsync<ReleaseRefused>(() =>
            store.PutFileAsync("v1.10.9.0", "AmneziaGeo-1.10.9.0-win-arm64.exe", Body(Executable(X64)), null, CancellationToken.None));
        var unknown = await Assert.ThrowsAsync<ReleaseRefused>(() =>
            store.PutFileAsync("v1.10.9.0", "evil.exe", Body(Executable(X64)), null, CancellationToken.None));
        var missing = await Assert.ThrowsAsync<ReleaseRefused>(() =>
            store.PutFileAsync("v1.10.8.0", "AmneziaGeo-1.10.8.0-win-x64.exe", Body(Executable(X64)), null, CancellationToken.None));
        var patched = await store.PutFileAsync("v1.10.9.0", "AmneziaGeo-1.10.9.0-win-x64.patch", Body(Patch()), "1.10.4.0", CancellationToken.None);
        var orphan = await store.PutFileAsync("v1.10.9.0", "AmneziaGeo-1.10.9.0-win-arm64.patch", Body(Patch()), null, CancellationToken.None);
        var manifest = ReleaseManifest.Parse(File.ReadAllText(Path.Combine(_folder.FullName, "releases", "v1.10.9.0", "update.json")))!;
        var withSetup = await store.PutFileAsync("v1.10.9.0", "AmneziaGeo-1.10.9.0-win-x64-fdd.exe", Body(Executable(X64)), null, CancellationToken.None);
        var later = ReleaseManifest.Parse(File.ReadAllText(Path.Combine(_folder.FullName, "releases", "v1.10.9.0", "update.json")))!;

        Assert.Single(taken.Files);
        Assert.Equal(512, taken.Files[0].Size);
        Assert.Equal(64, taken.Files[0].Sha256.Length);
        Assert.Equal(ReleaseSlots.Count - 1, taken.Missing.Count);
        Assert.Equal("bad-file", wrong.Code);
        Assert.Contains("AmneziaGeo-1.10.9.0-win-arm64.exe: the installer is built for x64", wrong.Message, StringComparison.Ordinal);
        Assert.Equal(new FileFault("AmneziaGeo-1.10.9.0-win-arm64.exe", FileFault.Arch, "x64", "arm64", wrong.File!.Message), wrong.File);
        Assert.Equal("bad-file-name", unknown.Code);
        Assert.Equal("release-missing", missing.Code);
        Assert.Equal("1.10.4.0", patched.Files.Single(file => file.Name.EndsWith("x64.patch", StringComparison.Ordinal)).From);
        Assert.Equal(3, orphan.Files.Count);
        Assert.Equal("nine", manifest.Description);
        Assert.Null(manifest.Setup);
        Assert.Single(manifest.Installers);
        Assert.Equal("AmneziaGeo-1.10.9.0-win-x64.patch", manifest.Installers[0].Patch?.Name);
        Assert.Equal("1.10.4.0", manifest.Installers[0].Patch?.From);
        Assert.Equal(4, withSetup.Files.Count);
        Assert.Equal("AmneziaGeo-1.10.9.0-win-x64-fdd.exe", later.Setup);
        Assert.Equal(2, later.Installers.Count);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder.FullName, "releases", "v1.10.9.0"), ".*"));
    }

    [Fact]
    public async Task TheNewestReleaseWithAFileAnswersEachChannel()
    {
        var store = new ReleaseStore(_folder.FullName);
        await store.CreateAsync("1.10.9.0", "stable", null, CancellationToken.None);
        await store.CreateAsync("1.10.10.0", "beta", null, CancellationToken.None);
        await store.CreateAsync("1.10.11.0", "test", null, CancellationToken.None);
        await store.CreateAsync("1.10.12.0", "stable", null, CancellationToken.None);

        var empty = store.Latest("stable");
        await store.PutFileAsync("v1.10.9.0", "AmneziaGeo-1.10.9.0-android.apk", Body(Apk("arm64-v8a", "armeabi-v7a", "x86_64")), null, CancellationToken.None);
        await store.PutFileAsync("v1.10.10.0-beta.1", "AmneziaGeo-1.10.10.0-android.apk", Body(Apk("arm64-v8a", "armeabi-v7a", "x86_64")), null, CancellationToken.None);
        await store.PutFileAsync("v1.10.11.0-test.1", "AmneziaGeo-1.10.11.0-android.apk", Body(Apk("arm64-v8a", "armeabi-v7a", "x86_64")), null, CancellationToken.None);

        Assert.Null(empty);
        Assert.Equal("v1.10.9.0", store.Latest("stable")?.Tag);
        Assert.Equal("v1.10.10.0-beta.1", store.Latest("beta")?.Tag);
        Assert.Equal("v1.10.11.0-test.1", store.Latest("test")?.Tag);
        Assert.EndsWith(Path.Combine("v1.10.9.0", "update.json"), store.Resolve("stable", "update.json"), StringComparison.Ordinal);
        Assert.EndsWith("AmneziaGeo-1.10.10.0-android.apk", store.Resolve("beta", "AmneziaGeo-1.10.10.0-android.apk"), StringComparison.Ordinal);
        Assert.Null(store.Resolve("beta", "AmneziaGeo-1.10.10.0-android-arm64.apk"));
        Assert.Null(store.Resolve("stable", "release.json"));
        Assert.Null(store.Resolve("stable", "../v1.10.10.0-beta.1/update.json"));
        Assert.Null(store.Resolve("rc", "update.json"));
        Assert.NotNull(store.ResolveIn("v1.10.11.0-test.1", "update.json"));
        Assert.Null(store.ResolveIn("v1.10.11.0-test.1", "AmneziaGeo-1.10.11.0-win-x64.exe"));
        Assert.Null(store.ResolveIn("v9.9.9.9", "update.json"));
    }

    [Fact]
    public async Task AReleaseChangesItsChannelDescriptionAndStarts()
    {
        var store = new ReleaseStore(_folder.FullName);
        await store.CreateAsync("1.10.9.0", "test", null, CancellationToken.None);
        await store.PutFileAsync("v1.10.9.0-test.1", "AmneziaGeo-1.10.9.0-win-x64.exe", Body(Executable(X64)), null, CancellationToken.None);
        await store.PutFileAsync("v1.10.9.0-test.1", "AmneziaGeo-1.10.9.0-win-x64.patch", Body(Patch()), null, CancellationToken.None);

        var described = await store.ChangeAsync("v1.10.9.0-test.1", null, "ready", null, CancellationToken.None);
        var started = await store.ChangeAsync(
            "v1.10.9.0-test.1",
            null,
            null,
            new Dictionary<string, string> { ["AmneziaGeo-1.10.9.0-win-x64.patch"] = "1.10.4.0" },
            CancellationToken.None);
        var notAPatch = await Assert.ThrowsAsync<ReleaseRefused>(() => store.ChangeAsync(
            "v1.10.9.0-test.1",
            null,
            null,
            new Dictionary<string, string> { ["AmneziaGeo-1.10.9.0-win-x64.exe"] = "1.10.4.0" },
            CancellationToken.None));
        var moved = await store.ChangeAsync("v1.10.9.0-test.1", "stable", null, null, CancellationToken.None);
        var gone = await Assert.ThrowsAsync<ReleaseRefused>(() => store.ChangeAsync("v1.10.9.0-test.1", null, "x", null, CancellationToken.None));
        var manifest = ReleaseManifest.Parse(File.ReadAllText(Path.Combine(_folder.FullName, "releases", "v1.10.9.0", "update.json")))!;

        Assert.Equal("ready", described.Release.Description);
        Assert.Equal("1.10.4.0", started.Files.Single(file => file.Name.EndsWith(".patch", StringComparison.Ordinal)).From);
        Assert.Equal("bad-file-name", notAPatch.Code);
        Assert.Equal("v1.10.9.0", moved.Release.Tag);
        Assert.Equal("stable", moved.Release.Channel);
        Assert.Equal(2, moved.Files.Count);
        Assert.Equal(404, gone.Status);
        Assert.Equal("stable", manifest.Channel);
        Assert.Equal("ready", manifest.Description);
        Assert.Equal("1.10.4.0", manifest.Installers[0].Patch?.From);
        Assert.Equal("v1.10.9.0", store.Latest("stable")?.Tag);
    }

    [Fact]
    public async Task AFileAndAReleaseAreDropped()
    {
        var store = new ReleaseStore(_folder.FullName);
        await store.CreateAsync("1.10.9.0", "stable", null, CancellationToken.None);
        await store.PutFileAsync("v1.10.9.0", "AmneziaGeo-1.10.9.0-win-x64.exe", Body(Executable(X64)), null, CancellationToken.None);
        await store.PutFileAsync("v1.10.9.0", "AmneziaGeo-1.10.9.0-win-x64.patch", Body(Patch()), null, CancellationToken.None);

        var dropped = await store.DropFileAsync("v1.10.9.0", "AmneziaGeo-1.10.9.0-win-x64.exe", CancellationToken.None);
        var twice = await Assert.ThrowsAsync<ReleaseRefused>(() => store.DropFileAsync("v1.10.9.0", "AmneziaGeo-1.10.9.0-win-x64.exe", CancellationToken.None));
        var manifest = ReleaseManifest.Parse(File.ReadAllText(Path.Combine(_folder.FullName, "releases", "v1.10.9.0", "update.json")))!;
        await store.DeleteAsync("v1.10.9.0", CancellationToken.None);
        var gone = await Assert.ThrowsAsync<ReleaseRefused>(() => store.DeleteAsync("v1.10.9.0", CancellationToken.None));

        Assert.Single(dropped.Files);
        Assert.Equal("file-missing", twice.Code);
        Assert.Empty(manifest.Installers);
        Assert.False(Directory.Exists(Path.Combine(_folder.FullName, "releases", "v1.10.9.0")));
        Assert.Equal(404, gone.Status);
        Assert.Equal(0, store.List(1, 10).Total);
    }

    [Fact]
    public async Task AnArchiveFillsAReleaseWhenItsManifestAgrees()
    {
        var store = new ReleaseStore(_folder.FullName);
        await store.CreateAsync("1.10.9.0", "stable", null, CancellationToken.None);
        var exe = Executable(X64);
        var apk = Apk("arm64-v8a");
        var files = new List<ReleaseFileInfo>
        {
            new("AmneziaGeo-1.10.9.0-win-x64.exe", exe.Length, Sha(exe), string.Empty),
            new("AmneziaGeo-1.10.9.0-android-arm64.apk", apk.Length, Sha(apk), string.Empty),
        };
        var manifest = ReleaseManifest.Build(new ReleaseTag(Nine, ReleaseChannel.Stable, 0), "from the archive", files).Serialize();

        var filled = await store.ImportAsync(
            "v1.10.9.0",
            Body(Zip(
                ("v1.10.9.0/update.json", Encoding.UTF8.GetBytes(manifest)),
                ("v1.10.9.0/AmneziaGeo-1.10.9.0-win-x64.exe", exe),
                ("v1.10.9.0/AmneziaGeo-1.10.9.0-android-arm64.apk", apk))),
            CancellationToken.None);
        var lied = await Assert.ThrowsAsync<ReleaseRefused>(() => store.ImportAsync(
            "v1.10.9.0",
            Body(Zip(
                ("update.json", Encoding.UTF8.GetBytes(manifest)),
                ("AmneziaGeo-1.10.9.0-win-x64.exe", Executable(X64).Concat(new byte[] { 1 }).ToArray()),
                ("AmneziaGeo-1.10.9.0-android-arm64.apk", apk))),
            CancellationToken.None));
        var stranger = await Assert.ThrowsAsync<ReleaseRefused>(() => store.ImportAsync(
            "v1.10.9.0",
            Body(Zip(("AmneziaGeo-1.10.9.0-win-x64.exe", exe), ("notes.txt", [1]))),
            CancellationToken.None));
        var wrongVersion = await Assert.ThrowsAsync<ReleaseRefused>(() => store.ImportAsync(
            "v1.10.9.0",
            Body(Zip(("update.json", Encoding.UTF8.GetBytes(manifest.Replace("1.10.9.0", "1.10.8.0", StringComparison.Ordinal))), ("AmneziaGeo-1.10.9.0-win-x64.exe", exe))),
            CancellationToken.None));
        var notZip = await Assert.ThrowsAsync<ReleaseRefused>(() => store.ImportAsync("v1.10.9.0", Body(exe), CancellationToken.None));
        var bare = await store.ImportAsync("v1.10.9.0", Body(Zip(("AmneziaGeo-1.10.9.0-win-x64-fdd.exe", exe))), CancellationToken.None);

        Assert.Equal(2, filled.Files.Count);
        Assert.Equal("from the archive", filled.Release.Description);
        Assert.Equal("bad-archive", lied.Code);
        Assert.Contains("not the file the manifest names", lied.Message, StringComparison.Ordinal);
        Assert.Equal("bad-archive", stranger.Code);
        Assert.Contains("notes.txt", stranger.Message, StringComparison.Ordinal);
        Assert.Equal("bad-manifest", wrongVersion.Code);
        Assert.Equal("bad-archive", notZip.Code);
        Assert.Equal(3, bare.Files.Count);
        Assert.Equal(3, store.Read("v1.10.9.0").Files.Count);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder.FullName, "releases", "v1.10.9.0"), ".*"));
    }

    [Fact]
    public async Task AFolderMadeByHandIsScanned()
    {
        var home = Directory.CreateDirectory(Path.Combine(_folder.FullName, "releases", "v1.10.9.0-beta.1"));
        var exe = Executable(X64);
        File.WriteAllBytes(Path.Combine(home.FullName, "AmneziaGeo-1.10.9.0-win-x64.exe"), exe);
        File.WriteAllBytes(Path.Combine(home.FullName, "AmneziaGeo-1.10.9.0-win-x64.patch"), Patch());
        File.WriteAllBytes(Path.Combine(home.FullName, "stray.bin"), [1, 2, 3]);
        File.WriteAllText(
            Path.Combine(home.FullName, "update.json"),
            ReleaseManifest.Build(
                new ReleaseTag(Nine, ReleaseChannel.Beta, 1),
                "by hand",
                [new("AmneziaGeo-1.10.9.0-win-x64.exe", exe.Length, Sha(exe), string.Empty), new("AmneziaGeo-1.10.9.0-win-x64.patch", 1, "x", "1.10.4.0")]).Serialize());
        Directory.CreateDirectory(Path.Combine(_folder.FullName, "releases", "not-a-release"));
        var store = new ReleaseStore(_folder.FullName);

        var read = store.Read("v1.10.9.0-beta.1");

        Assert.Equal(1, store.List(1, 10).Total);
        Assert.Equal("by hand", read.Release.Description);
        Assert.Equal(2, read.Files.Count);
        Assert.Equal(Sha(exe), read.Files[0].Sha256);
        Assert.Equal("1.10.4.0", read.Files[1].From);
        Assert.True(File.Exists(Path.Combine(home.FullName, ReleaseStore.RecordName)));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TheRoutesServeTheManifestAndTakeFilesUnderTheToken()
    {
        var token = UpdateToken.Make();
        var store = new ReleaseStore(_folder.FullName);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.Run(async context =>
        {
            if (!await UpdateApi.TryAnswerAsync(context, "/u", store, asking => UpdateToken.Holds(asking, token)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var anyone = new HttpClient { BaseAddress = new Uri(address) };
        using var holder = new HttpClient { BaseAddress = new Uri(address) };
        holder.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var refused = await anyone.GetAsync("/u/status");
        using var none = await anyone.GetAsync("/u/stable/update.json");
        using var status = await holder.GetAsync("/u/status");
        using var made = await holder.PostAsJsonAsync("/u/releases", new ReleaseDraft("1.10.9.0", "stable", "nine"));
        using var put = await holder.PutAsync("/u/releases/v1.10.9.0/files/AmneziaGeo-1.10.9.0-win-x64.patch?from=1.10.4.0", new ByteArrayContent(Patch()));
        using var exe = await holder.PutAsync("/u/releases/v1.10.9.0/files/AmneziaGeo-1.10.9.0-win-x64.exe", new ByteArrayContent(Executable(X64)));
        using var wrong = await holder.PutAsync("/u/releases/v1.10.9.0/files/AmneziaGeo-1.10.9.0-win-arm64.exe", new ByteArrayContent(Executable(X64)));
        using var manifest = await anyone.GetAsync("/u/stable/update.json");
        using var beta = await anyone.GetAsync("/u/beta/update.json");
        using var ranged = new HttpRequestMessage(HttpMethod.Get, "/u/stable/AmneziaGeo-1.10.9.0-win-x64.exe");
        ranged.Headers.Range = new RangeHeaderValue(0, 1);
        using var piece = await anyone.SendAsync(ranged);
        using var byTag = await anyone.GetAsync("/u/releases/v1.10.9.0/AmneziaGeo-1.10.9.0-win-x64.patch");
        using var stray = await anyone.GetAsync("/u/stable/release.json");
        using var list = await holder.GetAsync("/u/releases?page=1&size=5");
        using var changed = await holder.PatchAsJsonAsync("/u/releases/v1.10.9.0", new ReleaseChange(null, "nine again", null));
        using var dropped = await holder.DeleteAsync("/u/releases/v1.10.9.0/files/AmneziaGeo-1.10.9.0-win-x64.patch");
        using var deleted = await holder.DeleteAsync("/u/releases/v1.10.9.0");
        using var after = await anyone.GetAsync("/u/stable/update.json");
        await app.StopAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(HttpStatusCode.OK, made.StatusCode);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exe.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var refusal = await wrong.Content.ReadFromJsonAsync<UpdateFailure>();
        Assert.Equal("bad-file", refusal?.Error);
        Assert.Equal(FileFault.Arch, refusal?.File?.Reason);
        Assert.Equal("x64", refusal?.File?.Found);
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal("application/json", manifest.Content.Headers.ContentType?.MediaType);
        Assert.True(manifest.Headers.CacheControl?.NoCache);
        var read = ReleaseManifest.Parse(await manifest.Content.ReadAsStringAsync());
        Assert.Equal("1.10.9.0", read?.Version);
        Assert.Equal("1.10.4.0", read?.Installers.Single().Patch?.From);
        Assert.Equal(HttpStatusCode.OK, beta.StatusCode);
        Assert.Equal(HttpStatusCode.PartialContent, piece.StatusCode);
        Assert.Equal("MZ"u8.ToArray(), await piece.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/vnd.microsoft.portable-executable", piece.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, byTag.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stray.StatusCode);
        Assert.Equal(1, (await list.Content.ReadFromJsonAsync<ReleasePage>())?.Total);
        Assert.Equal("nine again", (await changed.Content.ReadFromJsonAsync<ReleaseDetails>())?.Release.Description);
        Assert.Equal(HttpStatusCode.OK, dropped.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    private static ReleaseSlot Slot(string name) => ReleaseSlots.Find(Nine, name)!;

    private static MemoryStream Body(byte[] bytes) => new(bytes);

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    private string Written(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder.FullName, name);
        File.WriteAllBytes(path, bytes);

        return path;
    }

    // A portable executable of nothing but its headers.
    private static byte[] Executable(ushort machine)
    {
        var bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x3C), 0x80);
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x84), machine);

        return bytes;
    }

    // A Debian package of a control file alone.
    private static byte[] Package(string package, string arch)
    {
        var control = Encoding.UTF8.GetBytes($"Package: {package}\nVersion: 1.10.9.0\nArchitecture: {arch}\nMaintainer: nobody\nDescription: test\n");
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./control") { DataStream = new MemoryStream(control) });
        }

        using var ar = new MemoryStream();
        ar.Write("!<arch>\n"u8);
        Member(ar, "debian-binary", "2.0\n"u8.ToArray());
        Member(ar, "control.tar.gz", packed.ToArray());
        Member(ar, "data.tar.gz", [1, 2, 3]);

        return ar.ToArray();
    }

    private static void Member(MemoryStream ar, string name, byte[] data)
    {
        ar.Write(Encoding.ASCII.GetBytes($"{name,-16}{0,-12}{0,-6}{0,-6}{"100644",-8}{data.Length,-10}`\n"));
        ar.Write(data);
        if (data.Length % 2 == 1)
        {
            ar.WriteByte((byte)'\n');
        }
    }

    // An Android package of a manifest, a dex and one library per architecture.
    private static byte[] Apk(params string[] abis) =>
        Zip([("AndroidManifest.xml", [1]), ("classes.dex", [2]), .. abis.Select(abi => ($"lib/{abi}/libamneziawg.so", new byte[] { 3 }))]);

    private static byte[] Patch() => Zip(("list", "# amneziageo patch 1\nfiles 0\n"u8.ToArray()));

    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var packed = new MemoryStream();
        using (var zip = new ZipArchive(packed, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(data);
            }
        }

        return packed.ToArray();
    }
}
