using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class RoutingFlagDropTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-routing-flag-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task ATemplateThatForbadeRoutingIsKeptWithoutTheFlag()
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database}").Options);
        await using (db)
        {
            await db.GetService<IMigrator>().MigrateAsync("PresetIdentity");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Templates (Name, Entries, AllowedIps, Missed, Dns, Mtu, Keepalive, LocksRouting, Presets, CreatedUtc, UpdatedUtc) VALUES "
                + "('closed', '0.0.0.0/0', '0.0.0.0/0', '', '1.1.1.1', 1280, NULL, 1, '3, 5', '2026-10-03 12:00:00+00:00', '2026-10-03 12:00:00+00:00'), "
                + "('open', '10.0.0.0/8', '10.0.0.0/8', '', '', NULL, 25, 0, '', '2026-10-04 12:00:00+00:00', '2026-10-04 12:00:00+00:00')");

            await db.Database.MigrateAsync();

            var flags = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('Templates') WHERE name = 'LocksRouting'")
                .ToListAsync();
            var modes = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('Clients') WHERE name = 'Routing'")
                .ToListAsync();
            var held = await db.Templates.OrderBy(template => template.Id).ToListAsync();
            Assert.Equal([0], flags);
            Assert.Equal([0], modes);
            Assert.Equal(["closed", "open"], held.Select(template => template.Name));
            Assert.Equal(["3, 5", string.Empty], held.Select(template => template.Presets));
            Assert.Equal([1280, null], held.Select(template => template.Mtu));
            Assert.Equal([null, 25], held.Select(template => template.Keepalive));
        }
    }
}
