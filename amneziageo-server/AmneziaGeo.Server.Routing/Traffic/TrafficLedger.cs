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
}

/// <summary>
/// Holds the traffic the clients made today and tells which of them used up their daily limit.
/// </summary>
public sealed class TrafficLedger
{
    private readonly Lock _sync = new();

    private readonly TimeProvider _time;

    private readonly TrafficMeter _meter;

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
    public void Observe(IReadOnlyList<TunnelClient> clients, IReadOnlyList<TrafficReading> readings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(readings);

        lock (_sync)
        {
            _meter.Keep(clients.Select(client => client.Id).ToHashSet());
            _meter.Observe(readings, now);
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

            return new ClientTraffic(_meter.Rate(client.Id), used, Spent(client, used));
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
            return Spent(client, _meter.Used(client.Id));
        }
    }

    private static bool Spent(TunnelClient client, ClientUsage used) =>
        client.DailyLimit > 0 && used.Total >= (ulong)client.DailyLimit;
}
