using AmneziaGeo.Server.Core.Diagnostics;
using AmneziaGeo.Server.Dal;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// Moves the records of the journal of the panel into its file and keeps the file within its limits.
/// </summary>
public sealed class JournalHost : BackgroundService
{
    private static readonly TimeSpan Complaining = TimeSpan.FromMinutes(1);

    private readonly PanelJournal _journal;

    private readonly JournalRecords _records;

    private readonly JournalLimits _limits;

    private readonly TimeProvider _time;

    private readonly ILogger<JournalHost> _logger;

    private DateTimeOffset _complained = DateTimeOffset.MinValue;

    private long _written;

    private long _failed;

    /// <summary>
    /// ctor
    /// </summary>
    public JournalHost(
        PanelJournal journal,
        JournalRecords records,
        JournalLimits limits,
        TimeProvider time,
        ILogger<JournalHost> logger)
    {
        _journal = journal;
        _records = records;
        _limits = limits;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// How many records reached the file since the panel started.
    /// </summary>
    public long Written => Interlocked.Read(ref _written);

    /// <summary>
    /// How many records never reached the file.
    /// </summary>
    public long Lost => _journal.Dropped + Interlocked.Read(ref _failed);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        await Task.Yield();
        if (!Prepare())
        {
            _journal.StopHanding();
            return;
        }

        try
        {
            using var timer = new PeriodicTimer(JournalDefaults.Gather, _time);
            while (await timer.WaitForNextTickAsync(stopping).ConfigureAwait(false))
            {
                Flush();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Flush();
        }
    }

    private bool Prepare()
    {
        try
        {
            _records.Prepare();
            DatabaseFiles.Hide(_records.Location, _logger);
            _journal.Recall(_records.Latest(JournalDefaults.View));
            Trim();

            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "the journal of the panel could not open {File} and stays in memory", _records.Location);

            return false;
        }
    }

    private void Flush()
    {
        var batch = new List<JournalEntry>(JournalDefaults.Batch);
        var written = 0;
        while (_journal.Waiting.TryRead(out var entry))
        {
            batch.Add(entry);
            if (batch.Count == JournalDefaults.Batch)
            {
                written += Write(batch);
                batch.Clear();
            }
        }

        written += Write(batch);
        if (written > 0)
        {
            Trim();
        }
    }

    private int Write(List<JournalEntry> batch)
    {
        var count = batch.Count;
        if (count == 0)
        {
            return 0;
        }

        try
        {
            _records.Write(batch);
            Interlocked.Add(ref _written, count);

            return count;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            Interlocked.Add(ref _failed, count);
            Complain(() => _logger.LogWarning(ex, "the journal of the panel could not write {Count} records", count));

            return 0;
        }
    }

    private void Trim()
    {
        try
        {
            var removed = _records.Trim(_limits.MostRecords, _limits.MostBytes);
            if (removed > 0)
            {
                _logger.LogInformation("the journal of the panel let {Count} old records go", removed);
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            Complain(() => _logger.LogWarning(ex, "the journal of the panel could not let its old records go"));
        }
    }

    private void Complain(Action say)
    {
        var now = _time.GetUtcNow();
        if (now - _complained < Complaining)
        {
            return;
        }

        _complained = now;
        say();
    }
}
