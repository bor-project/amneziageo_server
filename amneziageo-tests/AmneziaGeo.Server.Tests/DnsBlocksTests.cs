using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class DnsBlocksTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-dns-blocks-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task AResolverSavedWithTheSwitchesKeepsItsSettingsWithoutThem()
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database}").Options);
        await using (db)
        {
            await db.GetService<IMigrator>().MigrateAsync("AddConfigDefault");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Resolver (Id, IsEnabled, Port, Upstreams, Outbound, Listen, NameMinutes, CacheSize, MinTtl, MaxTtl, Intercept, BlockDot, BlockDoh, UpdatedUtc) VALUES "
                + "(1, 1, 5300, 'https://9.9.9.9/dns-query,1.1.1.1', 'bor', '10.8.0.1', 30, 128, 30, 600, 0, 0, 0, '2026-10-19 10:00:00+00:00')");

            await db.Database.MigrateAsync();

            var switches = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('Resolver') WHERE name IN ('Intercept', 'BlockDot', 'BlockDoh')")
                .ToListAsync();
            var held = await new DnsStore(db).ReadAsync(CancellationToken.None);
            Assert.Equal([0], switches);
            Assert.True(held.IsEnabled);
            Assert.Equal(5300, held.Port);
            Assert.Equal(["https://9.9.9.9/dns-query", "1.1.1.1"], held.Upstreams);
            Assert.Equal("bor", held.Outbound);
            Assert.Equal(["10.8.0.1"], held.Listen);
            Assert.Equal((30, 128, 30, 600), (held.NameMinutes, held.CacheSize, held.MinTtl, held.MaxTtl));
        }
    }

    [Fact]
    public async Task AResolverSavedBeforeTheGuardsWereDroppedComesThroughEveryStep()
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database}").Options);
        await using (db)
        {
            await db.GetService<IMigrator>().MigrateAsync("ClientDisconnect");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Resolver (Id, IsEnabled, Port, Upstreams, Outbound, Listen, NameMinutes, CacheSize, MinTtl, MaxTtl, Intercept, BlockDot, BlockDoh, UpdatedUtc) VALUES "
                + "(1, 1, 5300, 'https://9.9.9.9/dns-query,1.1.1.1', 'bor', '10.8.0.1', 30, 128, 30, 600, 1, 1, 1, '2026-10-12 12:00:00+00:00')");

            await db.Database.MigrateAsync();

            var held = await new DnsStore(db).ReadAsync(CancellationToken.None);
            Assert.True(held.IsEnabled);
            Assert.Equal(5300, held.Port);
            Assert.Equal("bor", held.Outbound);
        }
    }
}
