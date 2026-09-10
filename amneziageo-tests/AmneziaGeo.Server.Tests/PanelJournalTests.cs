using AmneziaGeo.Server.Api.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public class PanelJournalTests
{
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
}
