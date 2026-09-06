namespace AmneziaGeo.Server.Core.Status;

/// <summary>
/// One point of the window the panel draws.
/// </summary>
public sealed record StatusPoint(
    double Cpu,
    long Memory,
    long Swap,
    long Storage,
    long Upload,
    long Download,
    int Sockets);

/// <summary>
/// The bytes carried since the boot and the bytes a second right now.
/// </summary>
public sealed record TrafficReport(long Sent, long Received, long Upload, long Download);

/// <summary>
/// The readings kept for the graphs, oldest first.
/// </summary>
public sealed record StatusWindow(
    int Step,
    IReadOnlyList<double> Cpu,
    IReadOnlyList<long> Memory,
    IReadOnlyList<long> Swap,
    IReadOnlyList<long> Storage,
    IReadOnlyList<long> Upload,
    IReadOnlyList<long> Download,
    IReadOnlyList<int> Sockets);

/// <summary>
/// The state of the host as the overview reads it.
/// </summary>
public sealed record OverviewReport(
    ProcessorFacts Processor,
    double Cpu,
    Portion Memory,
    Portion Swap,
    Portion Storage,
    TrafficReport Traffic,
    SocketCounts Sockets,
    TunnelFacts Tunnel,
    long HostUptime,
    long PanelUptime,
    long PanelMemory,
    int PanelThreads,
    IReadOnlyList<string> Addresses,
    StatusWindow Window);
