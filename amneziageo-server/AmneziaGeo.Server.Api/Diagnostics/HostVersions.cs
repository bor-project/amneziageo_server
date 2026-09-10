using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// Reads the versions of what the panel runs on.
/// </summary>
public static class HostVersions
{
    private const string OsRelease = "/etc/os-release";

    private const string KernelRelease = "/proc/sys/kernel/osrelease";

    private const string Wstunnel = "/usr/local/bin/wstunnel";

    private const string PrettyName = "PRETTY_NAME=";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Returns the name of the system the panel runs on.
    /// </summary>
    public static string Distribution()
    {
        if (!File.Exists(OsRelease))
        {
            return RuntimeInformation.OSDescription;
        }

        foreach (var line in File.ReadLines(OsRelease))
        {
            if (line.StartsWith(PrettyName, StringComparison.Ordinal))
            {
                return line[PrettyName.Length..].Trim('"');
            }
        }

        return RuntimeInformation.OSDescription;
    }

    /// <summary>
    /// Returns the release of the kernel the panel runs on.
    /// </summary>
    public static string Kernel() =>
        File.Exists(KernelRelease) ? File.ReadAllText(KernelRelease).Trim() : Environment.OSVersion.VersionString;

    /// <summary>
    /// Returns what the proxy binary tells of its version, or an empty string when it is not there.
    /// </summary>
    public static async Task<string> WstunnelAsync(CancellationToken ct)
    {
        if (!File.Exists(Wstunnel))
        {
            return string.Empty;
        }

        var start = new ProcessStartInfo(Wstunnel, "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(start);
        if (process is null)
        {
            return string.Empty;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Wait);
        try
        {
            var text = await process.StandardOutput.ReadToEndAsync(limit.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);

            return text.Trim();
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);

            return string.Empty;
        }
    }
}
