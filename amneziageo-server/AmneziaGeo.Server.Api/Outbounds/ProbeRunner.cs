using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Probe;

namespace AmneziaGeo.Server.Api.Outbounds;

/// <summary>
/// Sends the probe of one outbound and keeps what it came to.
/// </summary>
public sealed class ProbeRunner
{
    private readonly IProbeLink _link;

    private readonly ProbeLive _live;

    private readonly TimeProvider _time;

    private readonly ILogger<ProbeRunner> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public ProbeRunner(IProbeLink link, ProbeLive live, TimeProvider time, ILogger<ProbeRunner> logger)
    {
        _link = link;
        _live = live;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Sends the probe of an outbound and tells whether it changed sides.
    /// </summary>
    public async Task<bool> RunAsync(OutboundConfig outbound, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        if (outbound.Probe.Length == 0 || !DnsRules.Upstream(outbound.Probe, out var server))
        {
            return false;
        }

        var outcome = await _link
            .ReachAsync(server, outbound.Mark, ProbeDefaults.Name, ProbeDefaults.Wait, ct)
            .ConfigureAwait(false);
        var changed = _live.Keep(outbound.Name, outcome, _time.GetUtcNow());
        if (changed)
        {
            _logger.LogInformation(
                "the probe of '{Outbound}' to {Server} now says {Verdict}",
                outbound.Name,
                outbound.Probe,
                outcome == ProbeOutcome.Reached ? "it carries traffic" : "it carries nothing");
        }

        return changed;
    }

    /// <summary>
    /// Tells whether an outbound is due a probe.
    /// </summary>
    public bool IsDue(OutboundConfig outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        if (!outbound.IsEnabled || outbound.Probe.Length == 0)
        {
            return false;
        }

        var reading = _live.Find(outbound.Name);
        var every = outbound.ProbeEvery > 0 ? outbound.ProbeEvery : ProbeDefaults.Every;

        return reading is null || _time.GetUtcNow() - reading.At >= TimeSpan.FromSeconds(every);
    }
}
