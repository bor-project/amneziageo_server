using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// Puts a release on a host that runs the panel from its package.
/// </summary>
public sealed class PackageUpdater
{
    /// <summary>
    /// The largest package the panel downloads, in bytes.
    /// </summary>
    public const long MaxSize = 512L * 1024 * 1024;

    private const string StagePrefix = "stage-";

    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(60);

    private static readonly string[] Launchers = ["/usr/bin/systemd-run", "/bin/systemd-run"];

    private readonly HttpClient _http;

    /// <summary>
    /// ctor
    /// </summary>
    public PackageUpdater(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Returns why the host cannot take a release from the panel, empty when it can.
    /// </summary>
    public static string Blocker()
    {
        if (!Environment.IsPrivilegedProcess)
        {
            return "no-root";
        }

        return Launcher() is null ? "no-systemd" : string.Empty;
    }

    /// <summary>
    /// Downloads the package of a release for an architecture, checks it against the manifest and unpacks it,
    /// returning its installer.
    /// </summary>
    public async Task<string> StageAsync(UpdateOffer offer, string arch, string folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(offer);

        var manifest = offer.Manifest;
        var package = manifest.PackageFor(arch)
            ?? throw new InvalidDataException($"the release {manifest.Version} carries no package for {arch}");

        Directory.CreateDirectory(folder);
        Sweep(folder);

        var target = Path.Combine(folder, StagePrefix + manifest.Version);
        Directory.CreateDirectory(target);
        Room(target, package.Size);

        var archive = Path.Combine(target, package.Name);
        await DownloadAsync(new Uri(offer.Files, package.Name), archive, package, ct).ConfigureAwait(false);

        var unpacked = Path.Combine(target, "package");
        await UnpackAsync(archive, unpacked, ct).ConfigureAwait(false);
        File.Delete(archive);

        return Installer(unpacked)
            ?? throw new InvalidDataException($"the package of {manifest.Version} carries no install.sh");
    }

    /// <summary>
    /// Removes the packages unpacked for earlier updates.
    /// </summary>
    public static void Sweep(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var old in Directory.GetDirectories(folder, StagePrefix + "*"))
        {
            Directory.Delete(old, recursive: true);
        }
    }

    /// <summary>
    /// Starts the installer of a release apart from the panel, which the installer stops and starts over.
    /// </summary>
    public static async Task LaunchAsync(string installer, string log, string exit, string unit, CancellationToken ct)
    {
        var launcher = Launcher() ?? throw new InvalidOperationException("the host carries no systemd-run");
        var start = new ProcessStartInfo(launcher)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var word in Words(installer, log, exit, unit))
        {
            start.ArgumentList.Add(word);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("systemd-run did not start");
        var said = process.StandardError.ReadToEndAsync(ct);
        await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var error = await said.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"systemd-run did not start the update: {error.Trim()}");
        }
    }

    /// <summary>
    /// Returns the words systemd-run is given to run an installer apart and write down how it ended.
    /// </summary>
    public static IReadOnlyList<string> Words(string installer, string log, string exit, string unit) =>
    [
        "--unit=" + unit,
        "--collect",
        "--quiet",
        "--",
        "/bin/sh",
        "-c",
        "bash \"$0\" >> \"$1\" 2>&1; echo $? > \"$2\"",
        installer,
        log,
        exit,
    ];

    private static string? Launcher() => Launchers.FirstOrDefault(File.Exists);

    private static void Room(string folder, long size)
    {
        var wanted = Math.Max(size, 64L * 1024 * 1024) * 5;
        var free = FreeSpace(folder);
        if (free >= 0 && free < wanted)
        {
            throw new IOException($"{folder} has {free / (1024 * 1024)} megabytes free, the update needs {wanted / (1024 * 1024)}");
        }
    }

    private static long FreeSpace(string folder)
    {
        try
        {
            return new DriveInfo(folder).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private async Task DownloadAsync(Uri address, string path, UpdatePackage package, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(StallLimit);
        using var response = await _http
            .GetAsync(address, HttpCompletionOption.ResponseHeadersRead, limit.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{address} answered {(int)response.StatusCode}");
        }

        var ceiling = package.Size > 0 ? Math.Min(package.Size, MaxSize) : MaxSize;
        if (response.Content.Headers.ContentLength > ceiling)
        {
            throw new InvalidDataException($"{address} is larger than the manifest says");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var source = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false))
        using (var file = File.Create(path))
        {
            var chunk = new byte[81920];
            var total = 0L;
            var read = 0;
            while ((read = await source.ReadAsync(chunk, limit.Token).ConfigureAwait(false)) > 0)
            {
                limit.CancelAfter(StallLimit);
                total += read;
                if (total > ceiling)
                {
                    throw new InvalidDataException($"{address} is larger than the manifest says");
                }

                hash.AppendData(chunk, 0, read);
                await file.WriteAsync(chunk.AsMemory(0, read), limit.Token).ConfigureAwait(false);
            }
        }

        var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(digest, package.Sha256, StringComparison.Ordinal))
        {
            File.Delete(path);
            throw new InvalidDataException($"{package.Name} does not match the digest the manifest names");
        }
    }

    private static async Task UnpackAsync(string archive, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, target, overwriteFiles: true, ct).ConfigureAwait(false);
    }

    private static string? Installer(string unpacked)
    {
        var direct = Path.Combine(unpacked, "install.sh");
        if (File.Exists(direct))
        {
            return direct;
        }

        return Directory.GetDirectories(unpacked)
            .Select(folder => Path.Combine(folder, "install.sh"))
            .FirstOrDefault(File.Exists);
    }
}
