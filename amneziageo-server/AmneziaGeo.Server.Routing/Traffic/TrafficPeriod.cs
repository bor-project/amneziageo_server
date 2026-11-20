using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Routing.Traffic;

public sealed record TrafficPeriod(
    long ClientId,
    string Period,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndsUtc,
    ClientUsage Used,
    ClientUsage Seen,
    string Generation,
    DateTimeOffset ObservedUtc);

public static class TrafficPeriods
{
    public static readonly IReadOnlyList<string> Kinds = ["day", "week", "month"];

    public static (DateTimeOffset Start, DateTimeOffset End) At(string period, DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        var day = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
        return period switch
        {
            "day" => (day, day.AddDays(1)),
            "week" => Week(day),
            "month" => Month(day),
            _ => throw new ArgumentOutOfRangeException(nameof(period)),
        };
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Week(DateTimeOffset day)
    {
        var start = day.AddDays(-((int)day.DayOfWeek + 6) % 7);
        return (start, start.AddDays(7));
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Month(DateTimeOffset day)
    {
        var start = new DateTimeOffset(day.Year, day.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (start, start.AddMonths(1));
    }
}

public sealed class TrafficPeriodMeter
{
    private readonly Dictionary<(long Client, string Period), TrafficPeriod> _rows = [];
    private readonly HashSet<(long Client, string Period)> _dirty = [];
    private readonly HashSet<long> _missing = [];

    public void Load(IEnumerable<TrafficPeriod> rows)
    {
        foreach (var row in rows)
        {
            _rows[(row.ClientId, row.Period)] = row;
        }
    }

    public void Prime(TunnelClient client, ClientUsage seen, string generation, DateTimeOffset now)
    {
        foreach (var period in TrafficPeriods.Kinds)
        {
            if (!_rows.ContainsKey((client.Id, period)))
            {
                var window = TrafficPeriods.At(period, now);
                Put(new TrafficPeriod(client.Id, period, window.Start, window.End, default, seen, generation, now));
            }
        }
    }

    public void Observe(
        IReadOnlyList<TunnelClient> clients,
        IReadOnlyList<TrafficReading> readings,
        IReadOnlyDictionary<long, string> generations,
        DateTimeOffset now)
    {
        var ids = clients.Select(client => client.Id).ToHashSet();
        foreach (var key in _rows.Keys.Where(key => !ids.Contains(key.Client)).ToArray())
        {
            _rows.Remove(key);
            _dirty.Remove(key);
        }

        _missing.RemoveWhere(id => !ids.Contains(id));
        foreach (var reading in readings.Where(reading => ids.Contains(reading.ClientId)))
        {
            var seen = new ClientUsage(reading.Rx, reading.Tx);
            var generation = generations.GetValueOrDefault(reading.ClientId, string.Empty);
            foreach (var kind in TrafficPeriods.Kinds)
            {
                var key = (reading.ClientId, kind);
                var window = TrafficPeriods.At(kind, now);
                if (!_rows.TryGetValue(key, out var before))
                {
                    Put(new TrafficPeriod(reading.ClientId, kind, window.Start, window.End, default, seen, generation, now));
                    continue;
                }

                var renewed = !string.Equals(before.Generation, generation, StringComparison.Ordinal) || _missing.Contains(reading.ClientId);
                var added = renewed
                    ? seen
                    : new ClientUsage(Since(before.Seen.Rx, reading.Rx), Since(before.Seen.Tx, reading.Tx));
                var turns = window.Start > before.StartedUtc;
                var row = before with
                {
                    StartedUtc = turns ? window.Start : before.StartedUtc,
                    EndsUtc = turns ? window.End : before.EndsUtc,
                    Used = Add(turns ? default : before.Used, added),
                    Seen = seen,
                    Generation = generation,
                    ObservedUtc = (turns || renewed || seen != before.Seen) && now > before.ObservedUtc ? now : before.ObservedUtc,
                };
                Put(row);
            }

            _missing.Remove(reading.ClientId);
        }

        foreach (var key in _rows.Keys.ToArray())
        {
            var before = _rows[key];
            var window = TrafficPeriods.At(key.Period, now);
            if (window.Start > before.StartedUtc)
            {
                Put(before with { StartedUtc = window.Start, EndsUtc = window.End, Used = default });
            }
        }
    }

    public TrafficPeriod Of(long clientId, string period, DateTimeOffset now)
    {
        if (_rows.TryGetValue((clientId, period), out var row))
        {
            return row;
        }

        var window = TrafficPeriods.At(period, now);
        return new TrafficPeriod(clientId, period, window.Start, window.End, default, default, string.Empty, now);
    }

    public TrafficPeriod Reset(long clientId, string period, DateTimeOffset now) =>
        Of(clientId, period, now) with { Used = default };

    public void Replace(TrafficPeriod row) => Put(row);

    public void ForgetCounters(long clientId) => _missing.Add(clientId);

    public IReadOnlyList<TrafficPeriod> Pending() => [.. _dirty.Where(_rows.ContainsKey).Select(key => _rows[key])];

    public void Written(IReadOnlyList<TrafficPeriod> rows)
    {
        foreach (var row in rows)
        {
            var key = (row.ClientId, row.Period);
            if (_rows.GetValueOrDefault(key) == row)
            {
                _dirty.Remove(key);
            }
        }
    }

    private void Put(TrafficPeriod row)
    {
        var key = (row.ClientId, row.Period);
        if (_rows.GetValueOrDefault(key) != row)
        {
            _rows[key] = row;
            _dirty.Add(key);
        }
    }

    private static ulong Since(ulong before, ulong now) => now >= before ? now - before : now;

    private static ClientUsage Add(ClientUsage one, ClientUsage two) => new(Sum(one.Rx, two.Rx), Sum(one.Tx, two.Tx));

    private static ulong Sum(ulong one, ulong two) => ulong.MaxValue - one < two ? ulong.MaxValue : one + two;
}
