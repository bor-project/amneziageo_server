using System.Threading.Channels;
using AmneziaGeo.Server.Core.Diagnostics;

namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// Keeps the latest records the panel writes to its log and hands each one on to the file of the journal.
/// </summary>
public sealed class PanelJournal : ILoggerProvider
{
    private readonly Lock _sync = new();

    private readonly JournalEntry[] _entries;

    private readonly TimeProvider _time;

    private readonly Channel<JournalEntry> _waiting;

    private int _next;

    private int _count;

    private long _dropped;

    private volatile bool _handing = true;

    /// <summary>
    /// ctor
    /// </summary>
    public PanelJournal(int capacity, TimeProvider time, int waiting = JournalDefaults.Queue)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(waiting, 1);
        ArgumentNullException.ThrowIfNull(time);

        _entries = new JournalEntry[capacity];
        _time = time;
        _waiting = Channel.CreateBounded<JournalEntry>(new BoundedChannelOptions(waiting)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>
    /// The records that wait for the file of the journal.
    /// </summary>
    public ChannelReader<JournalEntry> Waiting => _waiting.Reader;

    /// <summary>
    /// How many records never reached the line to the file.
    /// </summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Returns the kept records, the latest first.
    /// </summary>
    public IReadOnlyList<JournalEntry> Latest()
    {
        lock (_sync)
        {
            return Held();
        }
    }

    /// <summary>
    /// Keeps a record in place of the oldest one once the journal is full and puts it in line for the file.
    /// </summary>
    public void Keep(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_sync)
        {
            Put(entry);
        }

        if (_handing && !_waiting.Writer.TryWrite(entry))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>
    /// Puts the records read back from the file, the newest first, behind the ones kept since the start.
    /// </summary>
    public void Recall(IReadOnlyList<JournalEntry> older)
    {
        ArgumentNullException.ThrowIfNull(older);

        lock (_sync)
        {
            var merged = Held().Concat(older).Take(_entries.Length).Reverse().ToList();
            Array.Clear(_entries);
            _next = 0;
            _count = 0;
            foreach (var entry in merged)
            {
                Put(entry);
            }
        }
    }

    /// <summary>
    /// Stops putting records in line for the file and counts the waiting ones as dropped.
    /// </summary>
    public void StopHanding()
    {
        _handing = false;
        while (_waiting.Reader.TryRead(out _))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new JournalLogger(this, categoryName, _time);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private List<JournalEntry> Held()
    {
        var held = new List<JournalEntry>(_count);
        for (var back = 1; back <= _count; back++)
        {
            held.Add(_entries[(_next - back + _entries.Length) % _entries.Length]);
        }

        return held;
    }

    private void Put(JournalEntry entry)
    {
        _entries[_next] = entry;
        _next = (_next + 1) % _entries.Length;
        _count = Math.Min(_count + 1, _entries.Length);
    }
}

/// <summary>
/// Writes the records of one category into the journal of the panel.
/// </summary>
internal sealed class JournalLogger : ILogger
{
    private readonly PanelJournal _journal;

    private readonly string _category;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public JournalLogger(PanelJournal journal, string category, TimeProvider time)
    {
        _journal = journal;
        _category = Cut(category, JournalDefaults.LongestCategory);
        _time = time;
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        var fault = exception is null ? string.Empty : exception.GetType().Name + ": " + exception.Message;
        _journal.Keep(new JournalEntry(
            _time.GetUtcNow(),
            logLevel.ToString(),
            _category,
            Cut(formatter(state, exception), JournalDefaults.LongestMessage),
            Cut(fault, JournalDefaults.LongestFault)));
    }

    private static string Cut(string text, int longest) => text.Length <= longest ? text : text[..longest];
}
