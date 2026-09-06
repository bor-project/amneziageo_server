using AmneziaGeo.Server.Core.Status;

namespace AmneziaGeo.Server.Tests;

public class StatusTests
{
    private const string Stat = """
        cpu  77814 0 11315 20206700 2193 0 3361 0 0 0
        cpu0 10218 0 1517 2525120 387 0 1198 0 0 0
        intr 12345
        """;

    private const string MemInfo = """
        MemTotal:       15845456 kB
        MemFree:         7691216 kB
        MemAvailable:   14685240 kB
        SwapTotal:       4194300 kB
        SwapFree:        4094300 kB
        """;

    private const string NetDev = """
        Inter-|   Receive                                                |  Transmit
         face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
            lo: 8454857    6476    0    0    0     0          0         0  8454857    6476    0    0    0     0       0          0
          eth0: 185074834  583128    0 1179    0     0          0    263212 95335875  266173    0    0    0     0       0          0
        awgtest:       500       4    0    0    0     0          0         0      700       5    0    0    0     0       0          0
        """;

    private const string SockStat = """
        sockets: used 671
        TCP: inuse 8 orphan 0 tw 0 alloc 12 mem 320
        UDP: inuse 5 mem 0
        RAW: inuse 0
        """;

    private const string SockStat6 = """
        TCP6: inuse 4
        UDP6: inuse 3
        RAW6: inuse 1
        """;

    private const string CpuInfo = """
        processor	: 0
        model name	: 13th Gen Intel(R) Core(TM) i9-13900K
        cpu MHz		: 4000.000
        physical id	: 0
        core id		: 0
        cpu cores	: 2

        processor	: 1
        model name	: 13th Gen Intel(R) Core(TM) i9-13900K
        cpu MHz		: 2000.000
        physical id	: 0
        core id		: 0
        cpu cores	: 2

        processor	: 2
        model name	: 13th Gen Intel(R) Core(TM) i9-13900K
        cpu MHz		: 3000.000
        physical id	: 0
        core id		: 1
        cpu cores	: 2
        """;

    [Fact]
    public void TheHeadOfStatGivesTheBusyAndTheWholeTime()
    {
        var times = ProcText.Cpu(Stat);

        Assert.Equal(20301383, times.Total);
        Assert.Equal(92490, times.Busy);
    }

    [Fact]
    public void TheLoadIsTheBusyShareBetweenTwoReadings()
    {
        var load = CpuTimes.Load(new CpuTimes(100, 1000), new CpuTimes(150, 1200));

        Assert.Equal(25, load);
    }

    [Fact]
    public void ARestartedCounterLeavesTheLoadAtNothing()
    {
        Assert.Equal(0, CpuTimes.Load(new CpuTimes(100, 1000), new CpuTimes(0, 0)));
    }

    [Fact]
    public void TheAvailableMemoryIsWhatCountsAsFree()
    {
        var (memory, swap) = ProcText.Memory(MemInfo);

        Assert.Equal(15845456L * 1024, memory.Total);
        Assert.Equal((15845456L - 14685240) * 1024, memory.Used);
        Assert.Equal(4194300L * 1024, swap.Total);
        Assert.Equal(100000L * 1024, swap.Used);
    }

    [Fact]
    public void TheTrafficSumsEveryInterfaceButTheLoopback()
    {
        var traffic = ProcText.Traffic(NetDev);

        Assert.Equal(185074834 + 500, traffic.Received);
        Assert.Equal(95335875 + 700, traffic.Sent);
    }

    [Fact]
    public void TheRateIsTheGrowthOverTheSeconds()
    {
        var rate = TrafficTotals.Rate(new TrafficTotals(1000, 2000), new TrafficTotals(3000, 2600), 2);

        Assert.Equal(1000, rate.Received);
        Assert.Equal(300, rate.Sent);
    }

    [Fact]
    public void ACounterThatWentBackwardsGivesNoRate()
    {
        var rate = TrafficTotals.Rate(new TrafficTotals(1000, 2000), new TrafficTotals(10, 20), 2);

        Assert.Equal(0, rate.Received);
        Assert.Equal(0, rate.Sent);
    }

    [Fact]
    public void TheSocketsAddBothFamilies()
    {
        var sockets = ProcText.Sockets(SockStat, SockStat6);

        Assert.Equal(12, sockets.Tcp);
        Assert.Equal(8, sockets.Udp);
    }

    [Fact]
    public void TheUptimeIsTheFirstNumberOfTheFile()
    {
        Assert.Equal(TimeSpan.FromSeconds(25380.02), ProcText.Uptime("25380.02 202066.99\n"));
    }

    [Fact]
    public void TheProcessorCountsCoresApartFromThreads()
    {
        var processor = ProcText.Processor(CpuInfo);

        Assert.Equal("13th Gen Intel(R) Core(TM) i9-13900K", processor.Model);
        Assert.Equal(2, processor.Cores);
        Assert.Equal(3, processor.Threads);
        Assert.Equal(3000, processor.Megahertz);
    }

    [Fact]
    public void AnEmptyFileLeavesTheReadingAtNothing()
    {
        var (memory, swap) = ProcText.Memory(string.Empty);

        Assert.Equal(0, memory.Total);
        Assert.Equal(0, swap.Total);
        Assert.Equal(0, ProcText.Cpu(string.Empty).Total);
        Assert.Equal(0, ProcText.Traffic(string.Empty).Sent);
        Assert.Equal(0, ProcText.Sockets(string.Empty, string.Empty).Tcp);
        Assert.Equal(TimeSpan.Zero, ProcText.Uptime(string.Empty));
    }
}
