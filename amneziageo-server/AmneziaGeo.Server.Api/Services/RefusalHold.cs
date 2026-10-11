using System.Net;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// What one more websocket refused to an address comes to.
/// </summary>
/// <param name="Wait">How long the answer is held, nothing where it goes out at once.</param>
/// <param name="Count">The refusals of the address in a row, this one counted.</param>
/// <param name="Lasted">How long the row has run.</param>
/// <param name="First">Whether the row starts with this one.</param>
/// <param name="Begins">Whether the answers of the row are held from this one on.</param>
public readonly record struct RefusalNote(TimeSpan Wait, int Count, TimeSpan Lasted, bool First, bool Begins);

/// <summary>
/// Counts the websockets refused to each address and holds the answers to an address that keeps asking, so a client
/// that asks again at once after every refusal asks once in a while.
/// </summary>
public sealed class RefusalHold
{
    /// <summary>
    /// The refusals of a row that are answered at once.
    /// </summary>
    public const int Free = 3;

    /// <summary>
    /// The answers held for one address at a time; the ones past them go out at once.
    /// </summary>
    public const int Parked = 4;

    private const int Kept = 4096;

    /// <summary>
    /// How long an answer is held.
    /// </summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often a front that is down is asked again while an answer is held.
    /// </summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The silence of an address after which its refusals are counted anew.
    /// </summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(60);

    private readonly Dictionary<IPAddress, Row> _rows = [];

    private readonly Lock _gate = new();

    private readonly TimeProvider _time;

    private readonly Func<TimeSpan, CancellationToken, Task> _sleep;

    /// <summary>
    /// ctor
    /// </summary>
    public RefusalHold(TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? sleep = null)
    {
        _time = time ?? TimeProvider.System;
        _sleep = sleep ?? Task.Delay;
    }

    /// <summary>
    /// Counts a websocket refused to an address and returns what it comes to; one whose front is down is held from
    /// the first, so it can be handed to the front again.
    /// </summary>
    public RefusalNote Note(IPAddress? address, bool down = false)
    {
        var key = Key(address);
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (!_rows.TryGetValue(key, out var row) || now - row.Last > Quiet)
            {
                if (_rows.Count >= Kept)
                {
                    Forget(now);
                }

                row = new Row { Since = now };
                _rows[key] = row;
            }

            row.Count++;
            row.Last = now;
            var keeps = row.Count > Free;
            if ((!keeps && !down) || row.Holding >= Parked)
            {
                return new RefusalNote(TimeSpan.Zero, row.Count, now - row.Since, row.Count == 1, false);
            }

            var begins = keeps && !row.Told;
            row.Told |= begins;
            row.Holding++;

            return new RefusalNote(Wait, row.Count, now - row.Since, row.Count == 1, begins);
        }
    }

    /// <summary>
    /// Returns the answers held for an address now.
    /// </summary>
    public int Waiting(IPAddress? address)
    {
        lock (_gate)
        {
            return _rows.TryGetValue(Key(address), out var row) ? row.Holding : 0;
        }
    }

    /// <summary>
    /// Waits the time an answer is held for.
    /// </summary>
    public Task WaitAsync(TimeSpan wait, CancellationToken ct) => _sleep(wait, ct);

    /// <summary>
    /// Takes down an answer held for an address that has gone out.
    /// </summary>
    public void Left(IPAddress? address)
    {
        lock (_gate)
        {
            if (_rows.TryGetValue(Key(address), out var row) && row.Holding > 0)
            {
                row.Holding--;
            }
        }
    }

    // Returns the address a row is kept under.
    private static IPAddress Key(IPAddress? address) =>
        address is null ? IPAddress.None : address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    // Drops the rows that fell silent, and every row where that leaves too many.
    private void Forget(DateTimeOffset now)
    {
        foreach (var key in _rows.Where(one => now - one.Value.Last > Quiet).Select(one => one.Key).ToList())
        {
            _rows.Remove(key);
        }

        if (_rows.Count >= Kept)
        {
            _rows.Clear();
        }
    }

    // The refusals of one address in a row.
    private sealed class Row
    {
        public DateTimeOffset Since { get; init; }

        public DateTimeOffset Last { get; set; }

        public int Count { get; set; }

        public int Holding { get; set; }

        public bool Told { get; set; }
    }
}
