namespace AmneziaGeo.Server.Core.Status;

/// <summary>
/// The processor of the host.
/// </summary>
public sealed record ProcessorFacts(string Model, int Cores, int Threads, double Megahertz);

/// <summary>
/// A taken part of a whole, in bytes.
/// </summary>
public sealed record Portion(long Used, long Total);

/// <summary>
/// The time the processor spent, in jiffies.
/// </summary>
public sealed record CpuTimes(long Busy, long Total)
{
    /// <summary>
    /// Gives the share of the span between two readings the processor was busy.
    /// </summary>
    public static double Load(CpuTimes before, CpuTimes after)
    {
        var span = after.Total - before.Total;
        if (span <= 0)
        {
            return 0;
        }

        return Math.Clamp((after.Busy - before.Busy) * 100d / span, 0, 100);
    }
}

/// <summary>
/// The bytes the interfaces carried since the boot.
/// </summary>
public sealed record TrafficTotals(long Received, long Sent)
{
    /// <summary>
    /// Gives the bytes a second between two readings.
    /// </summary>
    public static TrafficTotals Rate(TrafficTotals before, TrafficTotals after, double seconds) =>
        seconds <= 0
            ? new TrafficTotals(0, 0)
            : new TrafficTotals(Step(before.Received, after.Received, seconds), Step(before.Sent, after.Sent, seconds));

    private static long Step(long before, long after, double seconds) =>
        after <= before ? 0 : (long)((after - before) / seconds);
}

/// <summary>
/// The sockets the host holds open.
/// </summary>
public sealed record SocketCounts(int Tcp, int Udp);

/// <summary>
/// The kernel module the tunnels run on.
/// </summary>
public sealed record TunnelFacts(bool Loaded, string Version, int Interfaces);

/// <summary>
/// One reading of the host.
/// </summary>
public sealed record SystemReading(
    CpuTimes Cpu,
    Portion Memory,
    Portion Swap,
    Portion Storage,
    TrafficTotals Traffic,
    SocketCounts Sockets,
    ProcessorFacts Processor);
