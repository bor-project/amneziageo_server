using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Routing.Traffic;

/// <summary>
/// What a client moves now and made today, and whether its daily limit is used up.
/// </summary>
/// <param name="Rate">How fast the client moves bytes now.</param>
/// <param name="Used">The traffic the client made today.</param>
/// <param name="IsSpent">Whether the traffic of the day reached the daily limit.</param>
public sealed record ClientTraffic(TrafficRate Rate, ClientUsage Used, bool IsSpent)
{
    /// <summary>
    /// The traffic of a client nothing was counted for.
    /// </summary>
    public static readonly ClientTraffic None = new(default, default, false);

    public ClientLimitStatus? Limits { get; init; }

    public TrafficTotal? Total { get; init; }
}

public sealed record ClientTrafficSnapshot(DateOnly Day, ClientTraffic Traffic, TrafficObservation Observation);

/// <summary>
/// Holds the traffic the clients made today and tells which of them used up their daily limit.
/// </summary>
public sealed class TrafficLedger
{
    private readonly Lock _sync = new();

    public Lock CounterGate { get; } = new();

    private readonly TimeProvider _time;

    private readonly TrafficMeter _meter;

    private readonly TrafficPeriodMeter _periods = new();

    private readonly Dictionary<long, TrafficHistory> _history = [];

    private readonly Dictionary<long, TrafficLimits> _limits = [];

    private readonly Dictionary<long, (bool Applied, string Error)> _applications = [];

    private readonly Dictionary<string, TunnelClient> _clients = new(StringComparer.Ordinal);

    /// <summary>
    /// ctor
    /// </summary>
    public TrafficLedger(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _meter = new TrafficMeter(HostDay());
    }

    /// <summary>
    /// The day the traffic is counted over.
    /// </summary>
    public DateOnly Day
    {
        get
        {
            lock (_sync)
            {
                return _meter.Day;
            }
        }
    }

    /// <summary>
    /// Returns the day the clock of the host stands at.
    /// </summary>
    public DateOnly HostDay() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime);

    /// <summary>
    /// Takes the traffic of the day and the counters the database held.
    /// </summary>
    public void Load(IEnumerable<TrafficDay> days)
    {
        lock (_sync)
        {
            _meter.Load(days);
        }
    }

    /// <summary>
    /// Starts a day with nothing counted.
    /// </summary>
    public void Turn(DateOnly day)
    {
        lock (_sync)
        {
            _meter.Turn(day);
        }
    }

    /// <summary>
    /// Takes the counters the peers of the clients carry now.
    /// </summary>
    public void Observe(IReadOnlyList<TunnelClient> clients, IReadOnlyList<TrafficReading> readings, DateTimeOffset now,
        IReadOnlyDictionary<long, string>? generations = null)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(readings);

        lock (_sync)
        {
            _meter.Keep(clients.Select(client => client.Id).ToHashSet());
            var previous = readings.Select(one => one.ClientId).Distinct()
                .ToDictionary(id => id, _meter.Used);
            _meter.Observe(readings, now);
            _periods.Observe(clients, readings, generations ?? new Dictionary<long, string>(), now);
            foreach (var (id, before) in previous)
            {
                var after = _meter.Used(id);
                var added = new ClientUsage(after.Rx - Math.Min(after.Rx, before.Rx), after.Tx - Math.Min(after.Tx, before.Tx));
                _history[id] = History(id).Add(added, _meter.Day);
            }

            var kept = clients.Select(client => client.Id).ToHashSet();
            foreach (var id in _history.Keys.Where(id => !kept.Contains(id)).ToArray())
            {
                _history.Remove(id);
            }
        }
    }

    /// <summary>
    /// Returns the days of the clients counted since they were last written.
    /// </summary>
    public IReadOnlyList<TrafficDay> Pending()
    {
        lock (_sync)
        {
            return _meter.Pending();
        }
    }

    /// <summary>
    /// Marks days as written.
    /// </summary>
    public void Written(IReadOnlyList<TrafficDay> days)
    {
        lock (_sync)
        {
            _meter.Written(days);
        }
    }

    /// <summary>
    /// Returns what a client moves now and made today.
    /// </summary>
    public ClientTraffic Of(TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (_sync)
        {
            var used = _meter.Used(client.Id);

            return new ClientTraffic(_meter.Rate(client.Id), used, Blocked(client, used))
            {
                Limits = LimitStatus(client), Total = History(client.Id).Total(),
            };
        }
    }

    /// <summary>
    /// Returns the traffic a client made today.
    /// </summary>
    public ClientUsage Used(TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (_sync)
        {
            return _meter.Used(client.Id);
        }
    }

    /// <summary>
    /// Tells whether a client used up its daily limit.
    /// </summary>
    public bool IsSpent(TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (_sync)
        {
            return Blocked(client, _meter.Used(client.Id));
        }
    }

    public ClientTrafficSnapshot Snapshot(TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_sync)
        {
            var used = _meter.Used(client.Id);
            var traffic = new ClientTraffic(_meter.Rate(client.Id), used, Blocked(client, used))
            {
                Limits = LimitStatus(client), Total = History(client.Id).Total(),
            };
            return new ClientTrafficSnapshot(_meter.Day, traffic, _meter.Observation(client.Id));
        }
    }

    public void LoadHistory(IEnumerable<TrafficHistory> rows)
    {
        lock (_sync)
        {
            foreach (var row in rows)
            {
                _history[row.ClientId] = row;
            }
        }
    }

    public ClientUsage Cumulative(long clientId)
    {
        lock (_sync)
        {
            return History(clientId).Used;
        }
    }

    public void AcceptTrafficReset(long clientId, ClientUsage baseline, DateTimeOffset now)
    {
        lock (_sync)
        {
            _history[clientId] = History(clientId) with { Reset = baseline, ResetUtc = now };
        }
    }

    private TrafficHistory History(long clientId) =>
        _history.GetValueOrDefault(clientId) ?? new TrafficHistory(clientId, default, null, default, null);

    public TrafficObservation Observation(long clientId)
    {
        lock (_sync)
        {
            return _meter.Observation(clientId);
        }
    }

    public void SetLimits(IReadOnlyList<TunnelClient> clients, IReadOnlyList<ClientTemplate> templates)
    {
        lock (_sync)
        {
            _limits.Clear();
            _clients.Clear();
            var held = templates.ToDictionary(template => template.Id);
            foreach (var client in clients)
            {
                _clients[client.PublicKey] = client;
                var template = client.TemplateId is { } id ? held.GetValueOrDefault(id) : null;
                if (TrafficLimitPolicy.Resolve(client, template) is { } policy)
                {
                    _limits[client.Id] = policy;
                }
            }

            foreach (var id in _applications.Keys.Where(id => !_limits.ContainsKey(id)).ToArray())
            {
                _applications.Remove(id);
            }
        }
    }

    public void Prime(TunnelClient client, ClientUsage seen, string generation)
    {
        lock (_sync)
        {
            _periods.Prime(client, seen, generation, _time.GetUtcNow());
        }
    }

    public void LoadPeriods(IEnumerable<TrafficPeriod> rows)
    {
        lock (_sync)
        {
            _periods.Load(rows);
        }
    }

    public IReadOnlyList<TrafficPeriod> PendingPeriods()
    {
        lock (_sync)
        {
            return _periods.Pending();
        }
    }

    public void WrittenPeriods(IReadOnlyList<TrafficPeriod> rows)
    {
        lock (_sync)
        {
            _periods.Written(rows);
        }
    }

    public TrafficPeriod? ResetPeriod(TunnelClient client)
    {
        lock (_sync)
        {
            return _limits.TryGetValue(client.Id, out var policy) && policy.QuotaBytes > 0
                ? _periods.Reset(client.Id, policy.Period, _time.GetUtcNow()) : null;
        }
    }

    public void AcceptReset(TrafficPeriod row)
    {
        lock (_sync)
        {
            _periods.Replace(row);
            _periods.Written([row]);
        }
    }

    public void ForgetCounters(long clientId)
    {
        lock (_sync)
        {
            _periods.ForgetCounters(clientId);
        }
    }

    public void ForgetCounters(string publicKey)
    {
        lock (_sync)
        {
            if (_clients.TryGetValue(publicKey, out var client))
            {
                _periods.ForgetCounters(client.Id);
            }
        }
    }

    public bool CanCarry(string publicKey)
    {
        lock (_sync)
        {
            return !_clients.TryGetValue(publicKey, out var client)
                || (client.IsEnabled && !Blocked(client, _meter.Used(client.Id)));
        }
    }

    public void Applied(IReadOnlyList<TunnelClient> clients, bool done, string error)
    {
        lock (_sync)
        {
            foreach (var client in clients.Where(client => _limits.ContainsKey(client.Id)))
            {
                _applications[client.Id] = (done, done ? string.Empty : error);
            }
        }
    }

    private bool Blocked(TunnelClient client, ClientUsage used) =>
        _limits.ContainsKey(client.Id)
            ? LimitStatus(client)?.IsBlocked == true
            : client.DailyLimit > 0 && used.Total >= (ulong)client.DailyLimit;

    private ClientLimitStatus? LimitStatus(TunnelClient client)
    {
        if (!_limits.TryGetValue(client.Id, out var policy))
        {
            return null;
        }

        var row = _periods.Of(client.Id, policy.Period, _time.GetUtcNow());
        var total = ulong.MaxValue - row.Used.Rx < row.Used.Tx ? ulong.MaxValue : row.Used.Total;
        var exhausted = policy.QuotaBytes > 0 && total >= (ulong)policy.QuotaBytes;
        var throttle = exhausted && policy.QuotaAction == "throttle";
        var application = _applications.GetValueOrDefault(client.Id);
        var error = application.Error ?? string.Empty;
        return new ClientLimitStatus(policy, !client.OverrideLimits, client.OverrideLimits ? null : client.TemplateId,
            row.StartedUtc, row.EndsUtc, row.Used.Rx, row.Used.Tx, total,
            policy.QuotaBytes > 0 ? (ulong)policy.QuotaBytes - Math.Min(total, (ulong)policy.QuotaBytes) : null,
            exhausted, throttle, (exhausted && policy.QuotaAction == "block") || error.Length > 0,
            throttle ? Cap(policy.UploadBitsPerSecond, policy.ExceededUploadBitsPerSecond) : policy.UploadBitsPerSecond,
            throttle ? Cap(policy.DownloadBitsPerSecond, policy.ExceededDownloadBitsPerSecond) : policy.DownloadBitsPerSecond,
            application.Applied, error);
    }

    private static long Cap(long rate, long reduced) => rate > 0 ? Math.Min(rate, reduced) : reduced;
}
