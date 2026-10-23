using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class PresetLinesTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-preset-lines-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task ThePresetsHeldBeforeTheLinesKeepTheirEntries()
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database}").Options);
        await using (db)
        {
            await db.GetService<IMigrator>().MigrateAsync("TemplatePresetsOnly");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO RoutingPresets (Uid, Name, Proxy, Direct, Block, AllUdp, Full, IsDefault, ReadOnly, CreatedUtc, UpdatedUtc) VALUES "
                + "('0f8fad5b-d9cb-469f-a165-70867728950e', 'held', 'geosite:youtube, cidr:10.0.0.0/8', 'geoip:ru', '', 0, 0, 0, 0, "
                + "'2026-10-03 12:00:00+00:00', '2026-10-03 12:00:00+00:00')");

            await db.Database.MigrateAsync();

            var raw = await db.Database.SqlQueryRaw<string>("SELECT Proxy AS Value FROM RoutingPresets").SingleAsync();
            var read = Assert.Single(await new PresetStore(db).ListAsync(CancellationToken.None));
            Assert.Equal("geosite:youtube\ncidr:10.0.0.0/8", raw);
            Assert.Equal(["geosite:youtube", "cidr:10.0.0.0/8"], read.Proxy);
            Assert.Equal(["geoip:ru"], read.Direct);
            Assert.Empty(read.Block);
        }
    }
}
