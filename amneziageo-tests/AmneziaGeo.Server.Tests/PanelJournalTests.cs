using AmneziaGeo.Server.Api.Diagnostics;
using AmneziaGeo.Server.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public class PanelJournalTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheLatestRecordsComeFirstAndTheOldestLeave()
    {
        var journal = new PanelJournal(2, TimeProvider.System);
        var logger = journal.CreateLogger("panel");

        logger.LogInformation("one");
        logger.LogWarning("two");
        logger.LogError("three");

        var latest = journal.Latest();

        Assert.Equal(["three", "two"], latest.Select(entry => entry.Message));
        Assert.Equal("Error", latest[0].Level);
        Assert.Equal("panel", latest[0].Category);
    }

    [Fact]
    public void AFaultIsKeptByItsTypeAndMessage()
    {
        var journal = new PanelJournal(4, TimeProvider.System);

        journal.CreateLogger("panel").LogWarning(new InvalidOperationException("broken"), "failed");

        Assert.Equal("InvalidOperationException: broken", journal.Latest()[0].Fault);
    }

    [Fact]
    public void AnEmptyJournalReadsEmpty()
    {
        Assert.Empty(new PanelJournal(4, TimeProvider.System).Latest());
    }

    [Fact]
    public void EveryRecordWaitsForTheFileInTheOrderItCame()
    {
        var journal = new PanelJournal(2, TimeProvider.System);
        var logger = journal.CreateLogger("panel");

        logger.LogInformation("one");
        logger.LogWarning("two");
        logger.LogError("three");

        Assert.Equal(["one", "two", "three"], Waiting(journal).Select(entry => entry.Message));
    }

    [Fact]
    public void TheRecordsReadBackGoBehindTheOnesKeptSinceTheStart()
    {
        var journal = new PanelJournal(3, TimeProvider.System);
        journal.CreateLogger("panel").LogInformation("after the start");

        journal.Recall([Made("older 2"), Made("older 1"), Made("older 0")]);

        Assert.Equal(["after the start", "older 2", "older 1"], journal.Latest().Select(entry => entry.Message));
        Assert.Equal(["after the start"], Waiting(journal).Select(entry => entry.Message));
    }

    [Fact]
    public void ARecordPastAFullLineIsDroppedAndCounted()
    {
        var journal = new PanelJournal(4, TimeProvider.System, waiting: 2);
        var logger = journal.CreateLogger("panel");

        logger.LogInformation("one");
        logger.LogInformation("two");
        logger.LogInformation("three");

        Assert.Equal(1, journal.Dropped);
        Assert.Equal(["one", "two"], Waiting(journal).Select(entry => entry.Message));
        Assert.Equal(["three", "two", "one"], journal.Latest().Select(entry => entry.Message));
    }

    [Fact]
    public void AJournalWithoutItsFileKeepsItsRecordsInMemoryAlone()
    {
        var journal = new PanelJournal(4, TimeProvider.System);
        var logger = journal.CreateLogger("panel");
        logger.LogInformation("one");

        journal.StopHanding();
        logger.LogInformation("two");

        Assert.Empty(Waiting(journal));
        Assert.Equal(1, journal.Dropped);
        Assert.Equal(["two", "one"], journal.Latest().Select(entry => entry.Message));
    }

    [Fact]
    public void ALongMessageIsCutToTheLongestKept()
    {
        var journal = new PanelJournal(4, TimeProvider.System);

        journal.CreateLogger("panel").LogInformation("{Text}", new string('x', JournalDefaults.LongestMessage + 10));

        Assert.Equal(JournalDefaults.LongestMessage, journal.Latest()[0].Message.Length);
    }

    private static List<JournalEntry> Waiting(PanelJournal journal)
    {
        var waiting = new List<JournalEntry>();
        while (journal.Waiting.TryRead(out var entry))
        {
            waiting.Add(entry);
        }

        return waiting;
    }

    private static JournalEntry Made(string message) => new(Noon, "Information", "panel", message, string.Empty);
}
