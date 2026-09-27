namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// Holds the records of the connections that went out until it is known what came back to them, one record a connection.
/// </summary>
public sealed class AccessWatch
{
    private readonly Dictionary<AccessKey, Watched> _watched = [];

    private readonly Dictionary<AccessKey, Seen> _seen = [];

    private readonly List<AccessRecord> _done = [];

    private readonly int _most;

    /// <summary>
    /// ctor
    /// </summary>
    public AccessWatch(int most = AccessDefaults.MostWatched) => _most = most;

    /// <summary>
    /// How many connections wait for what comes back.
    /// </summary>
    public int Count => _watched.Count;

    /// <summary>
    /// Takes a new record, folding a packet that asks again for the same connection into the record it already has.
    /// </summary>
    public void Take(AccessRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var key = AccessKey.Of(record);
        var goes = record.Verdict is AccessVerdict.Out or AccessVerdict.Host;
        if (_watched.TryGetValue(key, out var watched))
        {
            if (goes)
            {
                watched.Ask(record);
                return;
            }

            _watched.Remove(key);
        }
        else if (_seen.TryGetValue(key, out var seen) && seen.Until > record.At && seen.Verdict == record.Verdict)
        {
            return;
        }

        if (!goes || _watched.Count >= _most)
        {
            _done.Add(record);
            Remember(key, record);
            return;
        }

        _watched[key] = new Watched(record);
    }

    /// <summary>
    /// Takes what came back to a connection.
    /// </summary>
    public void Hear(AccessAnswer answer, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (!_watched.TryGetValue(answer.Key, out var watched))
        {
            return;
        }

        if (answer.Outcome is { } outcome)
        {
            _watched.Remove(answer.Key);
            _done.Add(watched.Record with { Outcome = outcome });
            return;
        }

        watched.Take(at);
    }

    /// <summary>
    /// Returns the records whose outcome is known or whose time ran out, letting them go.
    /// </summary>
    public IReadOnlyList<AccessRecord> Due(DateTimeOffset now)
    {
        var due = new List<AccessRecord>(_done);
        _done.Clear();
        foreach (var (key, watched) in _watched.Where(pair => pair.Value.Due <= now).ToArray())
        {
            _watched.Remove(key);
            Remember(key, watched.Record);
            due.Add(watched.Record with { Outcome = watched.IsTaken ? AccessOutcome.Empty : AccessOutcome.Silent });
        }

        foreach (var key in _seen.Where(pair => pair.Value.Until <= now).Select(pair => pair.Key).ToArray())
        {
            _seen.Remove(key);
        }

        return due;
    }

    /// <summary>
    /// Returns every record held, the ones still waiting without an outcome, letting them go.
    /// </summary>
    public IReadOnlyList<AccessRecord> Flush()
    {
        var all = new List<AccessRecord>(_done);
        all.AddRange(_watched.Values.Select(watched => watched.Record));
        _done.Clear();
        _watched.Clear();
        _seen.Clear();

        return all;
    }

    private void Remember(AccessKey key, AccessRecord record)
    {
        if (_seen.Count < _most)
        {
            _seen[key] = new Seen(record.At + AccessDefaults.Repeat, record.Verdict);
        }
    }

    private static DateTimeOffset Later(DateTimeOffset one, DateTimeOffset other) => one > other ? one : other;

    private static DateTimeOffset Earlier(DateTimeOffset one, DateTimeOffset other) => one < other ? one : other;

    private sealed record Seen(DateTimeOffset Until, string Verdict);

    private sealed class Watched(AccessRecord record)
    {
        public AccessRecord Record { get; private set; } = record;

        public DateTimeOffset Due { get; private set; } = record.At + AccessDefaults.Answer;

        public bool IsTaken { get; private set; }

        public void Ask(AccessRecord again)
        {
            Record = again with { At = Record.At };
            Due = Earlier(Later(Due, again.At + AccessDefaults.Answer), Record.At + AccessDefaults.LongestAnswer);
        }

        public void Take(DateTimeOffset at)
        {
            IsTaken = true;
            Due = Later(Due, at + AccessDefaults.Answer);
        }
    }
}
