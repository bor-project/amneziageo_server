namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// One record of the log of the panel.
/// </summary>
public sealed record JournalEntry(DateTimeOffset Time, string Level, string Category, string Message, string Fault);

/// <summary>
/// Keeps the latest records the panel writes to its log.
/// </summary>
public sealed class PanelJournal : ILoggerProvider
{
    private readonly Lock _sync = new();

    private readonly JournalEntry[] _entries;

    private readonly TimeProvider _time;

    private int _next;

    private int _count;

    /// <summary>
    /// ctor
    /// </summary>
    public PanelJournal(int capacity, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentNullException.ThrowIfNull(time);

        _entries = new JournalEntry[capacity];
        _time = time;
    }

    /// <summary>
    /// Returns the kept records, the latest first.
    /// </summary>
    public IReadOnlyList<JournalEntry> Latest()
    {
        lock (_sync)
        {
            var latest = new List<JournalEntry>(_count);
            for (var back = 1; back <= _count; back++)
            {
                latest.Add(_entries[(_next - back + _entries.Length) % _entries.Length]);
            }

            return latest;
        }
    }

    /// <summary>
    /// Keeps a record in place of the oldest one once the journal is full.
    /// </summary>
    public void Keep(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_sync)
        {
            _entries[_next] = entry;
            _next = (_next + 1) % _entries.Length;
            _count = Math.Min(_count + 1, _entries.Length);
        }
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new JournalLogger(this, categoryName, _time);

    /// <inheritdoc/>
    public void Dispose()
    {
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
        _category = category;
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
        _journal.Keep(new JournalEntry(_time.GetUtcNow(), logLevel.ToString(), _category, formatter(state, exception), fault));
    }
}
