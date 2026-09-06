using System.Globalization;

namespace AmneziaGeo.Server.Core.Status;

/// <summary>
/// Takes the numbers out of the text the kernel keeps in /proc.
/// </summary>
public static class ProcText
{
    private static readonly char[] Blanks = [' ', '\t'];

    private static readonly char[] Breaks = ['\n'];

    /// <summary>
    /// Reads the processor times off the head of /proc/stat.
    /// </summary>
    public static CpuTimes Cpu(string stat)
    {
        foreach (var line in Lines(stat))
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = Parts(line);
            var total = 0L;
            var idle = 0L;
            for (var i = 1; i < parts.Length && i <= 8; i++)
            {
                var value = Number(parts[i]);
                total += value;
                if (i is 4 or 5)
                {
                    idle += value;
                }
            }

            return new CpuTimes(total - idle, total);
        }

        return new CpuTimes(0, 0);
    }

    /// <summary>
    /// Reads the memory and the swap off /proc/meminfo.
    /// </summary>
    public static (Portion Memory, Portion Swap) Memory(string meminfo)
    {
        var total = Kilobytes(meminfo, "MemTotal:");
        var free = Kilobytes(meminfo, "MemAvailable:");
        var swap = Kilobytes(meminfo, "SwapTotal:");
        var swapFree = Kilobytes(meminfo, "SwapFree:");

        return (new Portion(Math.Max(total - free, 0), total), new Portion(Math.Max(swap - swapFree, 0), swap));
    }

    /// <summary>
    /// Sums the bytes of every interface but the loopback off /proc/net/dev.
    /// </summary>
    public static TrafficTotals Traffic(string netdev)
    {
        var received = 0L;
        var sent = 0L;
        foreach (var line in Lines(netdev))
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            if (line[..colon].Trim() is "lo")
            {
                continue;
            }

            var parts = Parts(line[(colon + 1)..]);
            if (parts.Length < 9)
            {
                continue;
            }

            received += Number(parts[0]);
            sent += Number(parts[8]);
        }

        return new TrafficTotals(received, sent);
    }

    /// <summary>
    /// Counts the open sockets off /proc/net/sockstat and its sixth family.
    /// </summary>
    public static SocketCounts Sockets(string sockstat, string sockstat6) => new(
        InUse(sockstat, "TCP:") + InUse(sockstat6, "TCP6:"),
        InUse(sockstat, "UDP:") + InUse(sockstat6, "UDP6:"));

    /// <summary>
    /// Reads how long the host has been up off /proc/uptime.
    /// </summary>
    public static TimeSpan Uptime(string uptime)
    {
        var parts = Parts(uptime);

        return parts.Length > 0 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }

    /// <summary>
    /// Reads the model, the cores and the clock off /proc/cpuinfo.
    /// </summary>
    public static ProcessorFacts Processor(string cpuinfo)
    {
        var model = string.Empty;
        var threads = 0;
        var clock = 0d;
        var clocks = 0;
        var package = string.Empty;
        var cores = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in Lines(cpuinfo))
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "processor":
                    threads++;
                    break;
                case "model name" when model.Length == 0:
                    model = value;
                    break;
                case "cpu MHz" when double.TryParse(value, CultureInfo.InvariantCulture, out var megahertz):
                    clock += megahertz;
                    clocks++;
                    break;
                case "physical id":
                    package = value;
                    break;
                case "core id":
                    cores.Add(package + "/" + value);
                    break;
            }
        }

        return new ProcessorFacts(model, cores.Count > 0 ? cores.Count : threads, threads, clocks > 0 ? clock / clocks : 0);
    }

    private static long Kilobytes(string meminfo, string key)
    {
        foreach (var line in Lines(meminfo))
        {
            if (!line.StartsWith(key, StringComparison.Ordinal))
            {
                continue;
            }

            var parts = Parts(line[key.Length..]);

            return parts.Length > 0 ? Number(parts[0]) * 1024 : 0;
        }

        return 0;
    }

    private static int InUse(string sockstat, string key)
    {
        foreach (var line in Lines(sockstat))
        {
            if (!line.StartsWith(key, StringComparison.Ordinal))
            {
                continue;
            }

            var parts = Parts(line);
            for (var i = 1; i + 1 < parts.Length; i++)
            {
                if (parts[i] is "inuse")
                {
                    return (int)Number(parts[i + 1]);
                }
            }
        }

        return 0;
    }

    private static long Number(string text) => long.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string[] Parts(string line) => line.Split(Blanks, StringSplitOptions.RemoveEmptyEntries);

    private static string[] Lines(string text) => text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries);
}
