using AmneziaGeo.Server.Core.Diagnostics;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

public sealed class JournalRecordsTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "journal-" + Guid.NewGuid().ToString("N"));

    private readonly JournalRecords _records;

    public JournalRecordsTests()
    {
        _records = new JournalRecords(Path.Combine(_folder, JournalDefaults.FileName));
        _records.Prepare();
    }

    public void Dispose()
    {
        Pools.Clear(_records.Location);
        Directory.Delete(_folder, true);
    }

    [Fact]
    public void TheJournalLivesBesideTheDatabaseOfThePanel() =>
        Assert.Equal(
            Path.Combine(Path.GetFullPath("/var/lib/amneziageo-server"), "journal.db"),
            JournalRecords.PathNear("/var/lib/amneziageo-server/server.db"));

    [Fact]
    public void ARecordComesBackAsItWasWritten()
    {
        var entry = new JournalEntry(
            Noon.AddMilliseconds(250),
            "Warning",
            "AmneziaGeo.Server.Api.Status.ServiceWatch",
            "the service subscriptions is down",
            "IOException: gone");

        _records.Write([entry]);

        Assert.Equal(entry, Assert.Single(_records.Latest(10)));
    }

    [Fact]
    public void TheLatestRecordsComeBackTheNewestFirst()
    {
        _records.Write(Made(5));

        Assert.Equal(["record 5", "record 4"], _records.Latest(2).Select(entry => entry.Message));
    }

    [Fact]
    public void WithinTheLimitsNothingGoes()
    {
        _records.Write(Made(50));

        Assert.Equal(0, _records.Trim(100, long.MaxValue));
        Assert.Equal(50, _records.Count());
    }

    [Fact]
    public void PastTheCountTheOldestGoWithATenthToSpare()
    {
        _records.Write(Made(150));

        var removed = _records.Trim(100, long.MaxValue);

        Assert.Equal(60, removed);
        Assert.Equal(90, _records.Count());
        Assert.Equal("record 150", _records.Latest(1)[0].Message);
    }

    [Fact]
    public void ThePlaceOfTheRecordsPastTheCountGoesBackToTheHost()
    {
        _records.Write(Made(1000, new string('x', 1000)));
        var before = _records.Bytes();

        _records.Trim(100, long.MaxValue);

        Assert.Equal(90, _records.Count());
        Assert.True(_records.Bytes() < before / 5, $"{_records.Bytes()} of {before} bytes");
    }

    [Fact]
    public void PastTheSizeTheOldestGoUntilTheFileFits()
    {
        const long limit = 256L << 10;
        _records.Write(Made(1000, new string('x', 1000)));
        Assert.True(_records.Bytes() > limit);

        var removed = _records.Trim(long.MaxValue, limit);

        Assert.True(_records.Bytes() <= limit, $"{_records.Bytes()} bytes");
        Assert.Equal(1000 - _records.Count(), removed);
        Assert.StartsWith("record 1000x", _records.Latest(1)[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherStartKeepsTheRecords()
    {
        _records.Write(Made(3));

        new JournalRecords(_records.Location).Prepare();

        Assert.Equal(3, _records.Count());
    }

    private static List<JournalEntry> Made(int count, string tail = "") =>
    [
        .. Enumerable.Range(1, count)
            .Select(one => new JournalEntry(Noon.AddSeconds(one), "Information", "panel", $"record {one}{tail}", string.Empty)),
    ];
}
