using System.Diagnostics;

namespace AmneziaGeo.Server.Core.Status;

/// <summary>
/// Keeps the readings of the host and hands out the window the overview draws.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    /// <summary>
    /// Seconds between two readings.
    /// </summary>
    public const int Step = 2;

    /// <summary>
    /// How many readings the window holds.
    /// </summary>
    public const int Depth = 120;

    private static readonly SystemReading Nothing = new(
        new CpuTimes(0, 0),
        new Portion(0, 0),
        new Portion(0, 0),
        new Portion(0, 0),
        new TrafficTotals(0, 0),
        new SocketCounts(0, 0),
        new ProcessorFacts(string.Empty, 0, 0, 0));

    private static readonly StatusPoint Still = new(0, 0, 0, 0, 0, 0, 0);

    private readonly SystemProbe _probe = new();
    private readonly Lock _sync = new();
    private readonly Queue<StatusPoint> _points = new();
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _started;
    private SystemReading? _last;
    private DateTimeOffset _stamp;
    private ITimer? _timer;

    /// <summary>
    /// ctor
    /// </summary>
    public SystemMonitor(TimeProvider time)
    {
        _time = time;
        _started = time.GetUtcNow();
    }

    /// <summary>
    /// Starts the beat that fills the window.
    /// </summary>
    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        Beat();
        var span = TimeSpan.FromSeconds(Step);
        _timer = _time.CreateTimer(_ => Beat(), null, span, span);
    }

    /// <summary>
    /// Gives the state of the host as the overview reads it.
    /// </summary>
    public OverviewReport Report()
    {
        StatusPoint[] points;
        SystemReading reading;
        StatusPoint latest;
        lock (_sync)
        {
            points = [.. _points];
            reading = _last ?? Nothing;
            latest = points.Length > 0 ? points[^1] : Still;
        }

        using var panel = Process.GetCurrentProcess();

        return new OverviewReport(
            reading.Processor,
            latest.Cpu,
            reading.Memory,
            reading.Swap,
            reading.Storage,
            new TrafficReport(reading.Traffic.Sent, reading.Traffic.Received, latest.Upload, latest.Download),
            reading.Sockets,
            _probe.Tunnel(),
            (long)_probe.Uptime().TotalSeconds,
            (long)(_time.GetUtcNow() - _started).TotalSeconds,
            panel.WorkingSet64,
            panel.Threads.Count,
            _probe.Addresses(),
            new StatusWindow(
                Step,
                [.. points.Select(one => Math.Round(one.Cpu, 2))],
                [.. points.Select(one => one.Memory)],
                [.. points.Select(one => one.Swap)],
                [.. points.Select(one => one.Storage)],
                [.. points.Select(one => one.Upload)],
                [.. points.Select(one => one.Download)],
                [.. points.Select(one => one.Sockets)]));
    }

    /// <summary>
    /// Stops the beat.
    /// </summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private static StatusPoint Measure(SystemReading before, SystemReading after, double seconds)
    {
        var rate = TrafficTotals.Rate(before.Traffic, after.Traffic, seconds);

        return new StatusPoint(
            CpuTimes.Load(before.Cpu, after.Cpu),
            after.Memory.Used,
            after.Swap.Used,
            after.Storage.Used,
            rate.Sent,
            rate.Received,
            after.Sockets.Tcp + after.Sockets.Udp);
    }

    private void Beat()
    {
        var reading = _probe.Read();
        var now = _time.GetUtcNow();

        lock (_sync)
        {
            if (_last is not null)
            {
                _points.Enqueue(Measure(_last, reading, (now - _stamp).TotalSeconds));
                while (_points.Count > Depth)
                {
                    _points.Dequeue();
                }
            }

            _last = reading;
            _stamp = now;
        }
    }
}
