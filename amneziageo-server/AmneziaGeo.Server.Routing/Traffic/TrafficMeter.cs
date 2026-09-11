using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Routing.Traffic;

/// <summary>
/// Bytes a second in both directions.
/// </summary>
/// <param name="Rx">Bytes a second taken from the client.</param>
/// <param name="Tx">Bytes a second given to the client.</param>
public readonly record struct TrafficRate(double Rx, double Tx);

/// <summary>
/// The counters the peer of a client carries at one reading.
/// </summary>
/// <param name="ClientId">The client the peer belongs to.</param>
/// <param name="Rx">Bytes the peer took since it was laid.</param>
/// <param name="Tx">Bytes the peer was given since it was laid.</param>
public readonly record struct TrafficReading(long ClientId, ulong Rx, ulong Tx);

/// <summary>
/// The traffic of a client over one day and the counters of its peer read last.
/// </summary>
/// <param name="ClientId">The client the traffic belongs to.</param>
/// <param name="Day">The day the traffic was made on.</param>
/// <param name="Used">The traffic of the day.</param>
/// <param name="Seen">The counters of the peer read last.</param>
public sealed record TrafficDay(long ClientId, DateOnly Day, ClientUsage Used, ClientUsage Seen);

/// <summary>
/// Counts the traffic the clients make over a day out of the counters of their peers.
/// </summary>
public sealed class TrafficMeter
{
    private readonly Dictionary<long, Track> _tracks = [];

    /// <summary>
    /// ctor
    /// </summary>
    public TrafficMeter(DateOnly day) => Day = day;

    /// <summary>
    /// The day the traffic is counted over.
    /// </summary>
    public DateOnly Day { get; private set; }

    /// <summary>
    /// Takes the traffic of the day and the counters the database held.
    /// </summary>
    public void Load(IEnumerable<TrafficDay> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        foreach (var day in days)
        {
            var track = Take(day.ClientId);
            track.Seen = day.Seen;
            track.Known = true;
            if (day.Day == Day)
            {
                track.Used = day.Used;
            }
        }
    }

    /// <summary>
    /// Starts a day with nothing counted.
    /// </summary>
    public void Turn(DateOnly day)
    {
        Day = day;
        foreach (var track in _tracks.Values)
        {
            track.Used = default;
            track.Dirty = false;
        }
    }

    /// <summary>
    /// Adds what the peers counted since the readings before; a counter below the one read last started over.
    /// </summary>
    public void Observe(IReadOnlyList<TrafficReading> readings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var heard = new HashSet<long>();
        foreach (var reading in readings)
        {
            heard.Add(reading.ClientId);
            var track = Take(reading.ClientId);
            var counted = new ClientUsage(reading.Rx, reading.Tx);
            if (track.Known)
            {
                var added = new ClientUsage(Since(track.Seen.Rx, reading.Rx), Since(track.Seen.Tx, reading.Tx));
                track.Used = track.Used.Add(added);
                track.Rate = track.At is { } at && now > at ? Per(added, (now - at).TotalSeconds) : default;
                track.Dirty |= counted != track.Seen;
            }
            else
            {
                track.Dirty = true;
            }

            track.Seen = counted;
            track.Known = true;
            track.At = now;
        }

        foreach (var (id, track) in _tracks)
        {
            if (!heard.Contains(id))
            {
                track.Rate = default;
                track.At = null;
            }
        }
    }

    /// <summary>
    /// Forgets the clients the panel no longer holds.
    /// </summary>
    public void Keep(IReadOnlySet<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        foreach (var id in _tracks.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _tracks.Remove(id);
        }
    }

    /// <summary>
    /// Returns the traffic a client made over the day.
    /// </summary>
    public ClientUsage Used(long clientId) => _tracks.TryGetValue(clientId, out var track) ? track.Used : default;

    /// <summary>
    /// Returns how fast a client moved bytes between the last two readings.
    /// </summary>
    public TrafficRate Rate(long clientId) => _tracks.TryGetValue(clientId, out var track) ? track.Rate : default;

    /// <summary>
    /// Returns the days of the clients counted since they were last written.
    /// </summary>
    public IReadOnlyList<TrafficDay> Pending() =>
    [
        .. _tracks
            .Where(pair => pair.Value.Dirty)
            .Select(pair => new TrafficDay(pair.Key, Day, pair.Value.Used, pair.Value.Seen))
    ];

    /// <summary>
    /// Marks days as written.
    /// </summary>
    public void Written(IReadOnlyList<TrafficDay> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        foreach (var day in days)
        {
            if (day.Day == Day
                && _tracks.TryGetValue(day.ClientId, out var track)
                && track.Used == day.Used
                && track.Seen == day.Seen)
            {
                track.Dirty = false;
            }
        }
    }

    private Track Take(long clientId)
    {
        if (!_tracks.TryGetValue(clientId, out var track))
        {
            track = new Track();
            _tracks[clientId] = track;
        }

        return track;
    }

    private static ulong Since(ulong before, ulong now) => now >= before ? now - before : now;

    private static TrafficRate Per(ClientUsage added, double seconds) => new(added.Rx / seconds, added.Tx / seconds);

    private sealed class Track
    {
        public ClientUsage Used { get; set; }

        public ClientUsage Seen { get; set; }

        public bool Known { get; set; }

        public bool Dirty { get; set; }

        public DateTimeOffset? At { get; set; }

        public TrafficRate Rate { get; set; }
    }
}
