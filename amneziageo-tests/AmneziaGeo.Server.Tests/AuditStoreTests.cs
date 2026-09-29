using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Tests;

public class AuditStoreTests
{
    [Fact]
    public async Task TheTrailKeepsItsLatestRecordsAlone()
    {
        using var bench = new Bench();
        var audit = new AuditStore(bench.Db, 3);

        for (var one = 1; one <= 5; one++)
        {
            await audit.WriteAsync(
                bench.Clock.GetUtcNow(), null, AuthScheme.Password, "login.unknown", "user" + one, null, "10.0.0.1", CancellationToken.None);
        }

        var targets = await bench.Db.AuditEntries.AsNoTracking().OrderBy(entry => entry.Id).Select(entry => entry.Target).ToListAsync();
        Assert.Equal(["user3", "user4", "user5"], targets);
    }

    [Fact]
    public async Task ThePanelKeepsTenThousandRecordsOfItsTrail()
    {
        using var bench = new Bench();

        await bench.Audit.WriteAsync(
            bench.Clock.GetUtcNow(), null, AuthScheme.Password, "login.unknown", "someone", null, "10.0.0.1", CancellationToken.None);

        Assert.IsType<AuditStore>(bench.Audit);
        Assert.Equal(10_000, AuditStore.MostEntries);
        Assert.Equal(1, await bench.Db.AuditEntries.CountAsync());
    }
}
