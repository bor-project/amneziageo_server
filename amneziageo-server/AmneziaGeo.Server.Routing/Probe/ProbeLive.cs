using System.Collections.Concurrent;

namespace AmneziaGeo.Server.Routing.Probe;

/// <summary>
/// What the probes of the outbounds came to.
/// </summary>
public sealed class ProbeLive
{
    private readonly ConcurrentDictionary<string, ProbeReading> _readings = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns what the probes of an outbound came to, or null when it was never probed.
    /// </summary>
    public ProbeReading? Find(string name) => _readings.GetValueOrDefault(name);

    /// <summary>
    /// Keeps what a probe came to and tells whether the outbound changed sides.
    /// </summary>
    public bool Keep(string name, ProbeOutcome outcome, DateTimeOffset at, int falls = ProbeDefaults.Falls)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (outcome == ProbeOutcome.Skipped)
        {
            return false;
        }

        var held = _readings.GetValueOrDefault(name);
        var missed = outcome == ProbeOutcome.Missed ? (held?.Falls ?? 0) + 1 : 0;
        var reading = new ProbeReading(missed < Math.Max(falls, 1), missed, at);
        _readings[name] = reading;

        return (held?.IsReached ?? true) != reading.IsReached;
    }

    /// <summary>
    /// Drops what the probes of an outbound came to.
    /// </summary>
    public void Forget(string name) => _readings.TryRemove(name, out _);

    /// <summary>
    /// Drops what the probes came to for every outbound but the ones named.
    /// </summary>
    public void Hold(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var held = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var name in _readings.Keys.Where(one => !held.Contains(one)))
        {
            _readings.TryRemove(name, out _);
        }
    }
}
