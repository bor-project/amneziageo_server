using System.Collections.Concurrent;
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

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pending = new(StringComparer.Ordinal);

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

        if (_live.Begin(outbound) is not { } attempt || !DnsRules.Upstream(outbound.Probe, out var server))
        {
            return false;
        }

        var gate = _pending.GetOrAdd(outbound.Name, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_live.IsCurrent(attempt))
            {
                return false;
            }

            var outcome = await _link
                .ReachAsync(server, outbound.Mark, ProbeDefaults.Name, ProbeDefaults.Wait, ct)
                .ConfigureAwait(false);
            var result = _live.Complete(attempt, outcome, _time.GetUtcNow());
            if (result.Warning && result.Reading is { } reading)
            {
                _logger.LogWarning(
                    "the checks of '{Outbound}' are {Health}: {Missed} of {Attempts} unanswered",
                    outbound.Name, reading.Health, reading.Missed, reading.Attempts);
            }
            else if (result.Recovered)
            {
                _logger.LogInformation("the checks of '{Outbound}' recovered", outbound.Name);
            }

            return result.CarriesChanged;
        }
        finally
        {
            gate.Release();
        }
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

        var reading = _live.Find(outbound, _time.GetUtcNow());
        var every = outbound.ProbeEvery > 0 ? outbound.ProbeEvery : ProbeDefaults.Every;

        return reading is null || _time.GetUtcNow() - reading.At >= TimeSpan.FromSeconds(every);
    }
}
