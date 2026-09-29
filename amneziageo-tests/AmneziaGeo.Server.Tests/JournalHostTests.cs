using AmneziaGeo.Server.Api.Diagnostics;
using AmneziaGeo.Server.Core.Diagnostics;
using AmneziaGeo.Server.Dal;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class JournalHostTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "journal-" + Guid.NewGuid().ToString("N"));

    private readonly JournalRecords _records;

    public JournalHostTests()
    {
        _records = new JournalRecords(Path.Combine(_folder, JournalDefaults.FileName));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }

        if (File.Exists(_folder))
        {
            File.Delete(_folder);
        }
    }

    [Fact]
    public async Task TheJournalComesBackAfterARestart()
    {
        var first = new PanelJournal(10, TimeProvider.System);
        first.CreateLogger("panel").LogWarning("before the restart");
        await RunAsync(first, JournalLimits.Default);

        var second = new PanelJournal(10, TimeProvider.System);
        second.CreateLogger("panel").LogInformation("after the restart");
        await RunAsync(second, JournalLimits.Default, () => second.Latest().Count == 2);

        Assert.Equal(["after the restart", "before the restart"], second.Latest().Select(entry => entry.Message));
        Assert.Equal(["after the restart", "before the restart"], _records.Latest(10).Select(entry => entry.Message));
    }

    [Fact]
    public async Task TheFileKeepsToItsCount()
    {
        var journal = new PanelJournal(10, TimeProvider.System);
        var logger = journal.CreateLogger("panel");
        for (var one = 1; one <= 250; one++)
        {
            logger.LogInformation("record {One}", one);
        }

        await RunAsync(journal, new JournalLimits(100, long.MaxValue));

        Assert.Equal(90, _records.Count());
        Assert.Equal("record 250", _records.Latest(1)[0].Message);
    }

    [Fact]
    public async Task TheFileKeepsToItsSize()
    {
        const long limit = 512L << 10;
        var journal = new PanelJournal(10, TimeProvider.System);
        var logger = journal.CreateLogger("panel");
        for (var one = 1; one <= 3000; one++)
        {
            logger.LogInformation("record {One} {Tail}", one, new string('x', 500));
        }

        await RunAsync(journal, new JournalLimits(long.MaxValue, limit));

        Assert.True(_records.Bytes() <= limit, $"{_records.Bytes()} bytes");
        Assert.StartsWith("record 3000 ", _records.Latest(1)[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJournalWhoseFileCannotOpenStaysInMemory()
    {
        File.WriteAllText(_folder, "a file where the folder of the journal should be");
        var journal = new PanelJournal(10, TimeProvider.System);
        journal.CreateLogger("panel").LogInformation("kept in memory");

        await RunAsync(journal, JournalLimits.Default, () => journal.Dropped == 1);

        Assert.Equal(["kept in memory"], journal.Latest().Select(entry => entry.Message));
        Assert.False(journal.Waiting.TryRead(out _));
        Assert.Equal(1, journal.Dropped);
    }

    [Fact]
    public void TheLimitsComeFromTheSettingsAndNeverFallBelowTheLeast()
    {
        Assert.Equal(new JournalLimits(100_000, 32L << 20), JournalLimits.From(Settings()));
        Assert.Equal(
            new JournalLimits(1_000, 1L << 20),
            JournalLimits.From(Settings(("Journal:MostRecords", "10"), ("Journal:MostBytes", "1"))));
        Assert.Equal(
            new JournalLimits(5_000, 2L << 20),
            JournalLimits.From(Settings(("Journal:MostRecords", "5000"), ("Journal:MostBytes", "2097152"))));
    }

    // Runs a host over the file until it is ready, then stops it.
    private async Task RunAsync(PanelJournal journal, JournalLimits limits, Func<bool>? ready = null)
    {
        using var host = new JournalHost(journal, _records, limits, TimeProvider.System, NullLogger<JournalHost>.Instance);
        await host.StartAsync(CancellationToken.None);
        await Until(ready ?? (() => File.Exists(_records.Location)));
        await host.StopAsync(CancellationToken.None);
    }

    private static async Task Until(Func<bool> done)
    {
        for (var step = 0; step < 100 && !done(); step++)
        {
            await Task.Delay(50);
        }

        Assert.True(done());
    }

    private static IConfiguration Settings(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
}
