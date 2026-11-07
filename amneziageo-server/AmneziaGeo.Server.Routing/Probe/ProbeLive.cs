using System.Security.Cryptography;
using System.Text.Json;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Routing.Probe;

/// <summary>
/// Holds the results of outbound checks.
/// </summary>
public sealed class ProbeLive
{
    private readonly object _gate = new();

    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    private long _generation;

    private bool _registered;

    /// <summary>
    /// Returns the latest reading.
    /// </summary>
    public ProbeReading? Find(string name)
    {
        lock (_gate)
        {
            return _sessions.GetValueOrDefault(name)?.Reading;
        }
    }

    /// <summary>
    /// Returns the reading of the current configuration.
    /// </summary>
    public ProbeReading? Find(OutboundConfig outbound, DateTimeOffset? now = null)
    {
        if (!Checks(outbound))
        {
            return null;
        }

        var fingerprint = Fingerprint(outbound);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(outbound.Name, out var session) || session.Fingerprint != fingerprint)
            {
                return null;
            }

            return now is { } at && session.Reading is { } reading && at - reading.At > session.FreshFor
                ? reading with { Health = ProbeHealth.Unknown, Attempts = 0, Missed = 0 }
                : session.Reading;
        }
    }

    /// <summary>
    /// Registers the current configuration.
    /// </summary>
    public void Observe(OutboundConfig outbound)
    {
        var fingerprint = Fingerprint(outbound);
        lock (_gate)
        {
            _registered = true;
            Observe(outbound.Name, fingerprint, ProbeDefaults.FreshFor(outbound.ProbeEvery));
        }
    }

    /// <summary>
    /// Registers the configured outbounds.
    /// </summary>
    public bool Sync(IEnumerable<OutboundConfig> outbounds)
    {
        var held = outbounds.Select(one => (one.Name, Fingerprint: Fingerprint(one), FreshFor: ProbeDefaults.FreshFor(one.ProbeEvery))).ToArray();
        lock (_gate)
        {
            _registered = true;
            var changed = false;
            foreach (var one in held)
            {
                changed |= Observe(one.Name, one.Fingerprint, one.FreshFor);
            }

            foreach (var gone in _sessions.Keys.Where(name => held.All(one => one.Name != name)).ToArray())
            {
                changed |= _sessions[gone].Reading is not null;
                _sessions.Remove(gone);
            }

            return changed;
        }
    }

    /// <summary>
    /// Starts a check of the registered configuration.
    /// </summary>
    public ProbeAttempt? Begin(OutboundConfig outbound)
    {
        if (!Checks(outbound))
        {
            return null;
        }

        var fingerprint = Fingerprint(outbound);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(outbound.Name, out var session))
            {
                if (_registered)
                {
                    return null;
                }

                session = new Session(fingerprint, ++_generation, ProbeDefaults.FreshFor(outbound.ProbeEvery));
                _sessions[outbound.Name] = session;
            }

            return session.Fingerprint == fingerprint
                ? new ProbeAttempt(outbound.Name, session.Generation)
                : null;
        }
    }

    /// <summary>
    /// Tells whether a check still belongs to the current configuration.
    /// </summary>
    public bool IsCurrent(ProbeAttempt attempt)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(attempt.Name, out var session) && session.Generation == attempt.Generation;
        }
    }

    /// <summary>
    /// Keeps a result of the current configuration.
    /// </summary>
    public ProbeCompletion Complete(ProbeAttempt attempt, ProbeOutcome outcome, DateTimeOffset at, int falls = ProbeDefaults.Falls)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(attempt.Name, out var session) && session.Generation == attempt.Generation
                ? Complete(session, outcome, at, falls)
                : default;
        }
    }

    /// <summary>
    /// Keeps a check under its name.
    /// </summary>
    public bool Keep(string name, ProbeOutcome outcome, DateTimeOffset at, int falls = ProbeDefaults.Falls)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(name, out var session))
            {
                session = new Session(null, ++_generation);
                _sessions[name] = session;
            }

            return Complete(session, outcome, at, falls).CarriesChanged;
        }
    }

    /// <summary>
    /// Drops the checks of one outbound.
    /// </summary>
    public void Forget(string name)
    {
        lock (_gate)
        {
            _registered = true;
            _sessions.Remove(name);
        }
    }

    /// <summary>
    /// Drops the checks of removed outbounds.
    /// </summary>
    public void Hold(IEnumerable<string> names)
    {
        var held = new HashSet<string>(names, StringComparer.Ordinal);
        lock (_gate)
        {
            _registered = true;
            foreach (var name in _sessions.Keys.Where(one => !held.Contains(one)).ToArray())
            {
                _sessions.Remove(name);
            }
        }
    }

    private bool Observe(string name, string fingerprint, TimeSpan freshFor)
    {
        if (_sessions.TryGetValue(name, out var held) && held.Fingerprint == fingerprint)
        {
            return false;
        }

        _sessions[name] = new Session(fingerprint, ++_generation, freshFor);

        return held?.Reading is not null;
    }

    private static ProbeCompletion Complete(Session session, ProbeOutcome outcome, DateTimeOffset at, int falls)
    {
        if (outcome == ProbeOutcome.Skipped)
        {
            return default;
        }

        var before = session.Reading;
        if (before is not null && at - before.At > session.FreshFor)
        {
            session.Window.Clear();
            session.Successes = 0;
            session.Degraded = false;
        }

        var reached = outcome == ProbeOutcome.Reached;
        session.Window.Enqueue(reached);
        if (session.Window.Count > ProbeDefaults.Window)
        {
            session.Window.Dequeue();
        }

        session.Successes = reached ? session.Successes + 1 : 0;
        session.LastSuccess = reached ? at : session.LastSuccess;
        var missed = session.Window.Count(one => !one);
        var consecutive = reached ? 0 : (before?.Falls ?? 0) + 1;
        var isReached = consecutive < Math.Max(falls, 1);
        var below = missed * 100 < session.Window.Count * ProbeDefaults.DegradedPercent;
        var wasBad = session.Degraded;
        if (!isReached || session.Window.Count >= ProbeDefaults.MinAttempts && !below)
        {
            session.Degraded = true;
        }
        else if (session.Degraded && session.Successes >= ProbeDefaults.RecoverySuccesses && below)
        {
            session.Degraded = false;
        }

        var health = !isReached ? ProbeHealth.Unavailable
            : session.Degraded ? ProbeHealth.Degraded
            : session.Window.Any(one => one) ? ProbeHealth.Healthy
            : ProbeHealth.Unknown;
        var reading = new ProbeReading(isReached, consecutive, at)
        {
            Health = health,
            Attempts = session.Window.Count,
            Missed = missed,
            LastSuccess = session.LastSuccess,
        };
        session.Reading = reading;

        return new ProbeCompletion(true, (before?.IsReached ?? true) != isReached,
            !wasBad && session.Degraded, wasBad && !session.Degraded, reading);
    }

    private static bool Checks(OutboundConfig outbound) => outbound.IsEnabled && outbound.Probe.Length > 0;

    private static string Fingerprint(OutboundConfig outbound) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        outbound.Id, outbound.Kind, outbound.IsEnabled, outbound.Host, outbound.Port, outbound.Proxy,
        outbound.PrivateKey, outbound.PublicKey, outbound.PeerKey, outbound.PresharedKey, outbound.Address, outbound.Dns,
        outbound.Mtu, outbound.Keepalive, outbound.Mark, outbound.Table, outbound.Obfuscation,
        outbound.Probe, outbound.ProbeEvery,
    })));

    private sealed class Session
    {
        /// <summary>
        /// ctor
        /// </summary>
        public Session(string? fingerprint, long generation, TimeSpan? freshFor = null)
        {
            Fingerprint = fingerprint;
            Generation = generation;
            FreshFor = freshFor ?? ProbeDefaults.FreshFor(0);
        }

        public string? Fingerprint { get; }

        public long Generation { get; }

        public TimeSpan FreshFor { get; }

        public Queue<bool> Window { get; } = new();

        public ProbeReading? Reading { get; set; }

        public DateTimeOffset? LastSuccess { get; set; }

        public int Successes { get; set; }

        public bool Degraded { get; set; }
    }
}
