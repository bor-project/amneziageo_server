using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class PresetIdentityTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-presets-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task PresetsHeldBeforeIdentifiersGetOneEach()
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database}").Options);
        await using (db)
        {
            await db.GetService<IMigrator>().MigrateAsync("RoutingPresets");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO RoutingPresets (Name, Proxy, Direct, Block, AllUdp, Full, CreatedUtc, UpdatedUtc) VALUES "
                + "('first', 'geosite:youtube', '', '', 0, 0, '2026-10-03 12:00:00+00:00', '2026-10-03 12:00:00+00:00'), "
                + "('second', '', 'geoip:ru', '', 1, 0, '2026-10-04 12:00:00+00:00', '2026-10-04 12:00:00+00:00')");

            await db.Database.MigrateAsync();

            var held = await db.Database
                .SqlQueryRaw<string>("SELECT Uid AS Value FROM RoutingPresets ORDER BY Id")
                .ToListAsync();
            var flags = await db.Database
                .SqlQueryRaw<int>("SELECT IsDefault AS Value FROM RoutingPresets ORDER BY Id")
                .ToListAsync();
            Assert.Equal(2, held.Count);
            Assert.All(held, uid => Assert.True(Guid.TryParseExact(uid, "D", out _), uid));
            Assert.NotEqual(held[0], held[1]);
            Assert.Equal([0, 0], flags);
        }
    }
}
