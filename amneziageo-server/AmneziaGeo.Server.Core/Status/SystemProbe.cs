using System.Net;
using System.Net.NetworkInformation;

namespace AmneziaGeo.Server.Core.Status;

/// <summary>
/// Takes the readings of the host off /proc, /sys and the runtime.
/// </summary>
public sealed class SystemProbe
{
    private const string ModulePath = "/sys/module/amneziawg";
    private const string LinkPath = "/sys/class/net";
    private const string DeviceType = "DEVTYPE=amneziawg";

    /// <summary>
    /// Reads the counters that move from beat to beat.
    /// </summary>
    public SystemReading Read()
    {
        var (memory, swap) = ProcText.Memory(Text("/proc/meminfo"));

        return new SystemReading(
            ProcText.Cpu(Text("/proc/stat")),
            memory,
            swap,
            Storage(),
            ProcText.Traffic(Text("/proc/net/dev")),
            ProcText.Sockets(Text("/proc/net/sockstat"), Text("/proc/net/sockstat6")),
            ProcText.Processor(Text("/proc/cpuinfo")));
    }

    /// <summary>
    /// Reads how long the host has been up.
    /// </summary>
    public TimeSpan Uptime() => ProcText.Uptime(Text("/proc/uptime"));

    /// <summary>
    /// Reads the module the tunnels run on and the interfaces it carries.
    /// </summary>
    public TunnelFacts Tunnel() => new(Directory.Exists(ModulePath), Text(ModulePath + "/version").Trim(), Interfaces());

    /// <summary>
    /// Lists the addresses the host answers on.
    /// </summary>
    public IReadOnlyList<string> Addresses() =>
    [
        .. NetworkInterface.GetAllNetworkInterfaces()
            .Where(one => one.OperationalStatus == OperationalStatus.Up)
            .Where(one => one.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(one => one.GetIPProperties().UnicastAddresses)
            .Select(one => one.Address)
            .Where(one => !IPAddress.IsLoopback(one) && !one.IsIPv6LinkLocal)
            .Select(one => one.ToString())
            .Distinct(StringComparer.Ordinal),
    ];

    private static Portion Storage()
    {
        try
        {
            var drive = new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\" : "/");

            return new Portion(drive.TotalSize - drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return new Portion(0, 0);
        }
    }

    private static int Interfaces()
    {
        if (!Directory.Exists(LinkPath))
        {
            return 0;
        }

        var count = 0;
        foreach (var link in Directory.EnumerateDirectories(LinkPath))
        {
            if (Text(Path.Combine(link, "uevent")).Contains(DeviceType, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static string Text(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
