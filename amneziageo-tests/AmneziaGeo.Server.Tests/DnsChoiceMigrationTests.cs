using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class DnsChoiceMigrationTests : IDisposable
{
    private const string At = "'2026-10-21 10:00:00+00:00'";

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-dns-choice-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task ATemplateThatNamedServersKeepsThemAndTheRestGoAutomatic()
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database}").Options);
        await using (db)
        {
            await db.GetService<IMigrator>().MigrateAsync("ClientResolverHanded");
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Templates (Id, AllowedIps, CreatedUtc, Dns, Entries, Missed, Name, Presets, UpdatedUtc, StandIns) VALUES "
                + "(1, '', " + At + ", '9.9.9.9', '', '', 'own', '', " + At + ", 0), "
                + "(2, '', " + At + ", ' ', '', '', 'bare', '', " + At + ", 0)");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Clients (Address, ConfigId, CreatedUtc, DailyLimit, Inbound, IsEnabled, Name, Note, PresharedKey, PrivateKey, PublicKey, SubscriptionId, TemplateId, UpdatedUtc) VALUES "
                + "('10.8.0.2/32', 1, " + At + ", 0, 0, 1, 'anna', '', '', '', 'k1', '', 1, " + At + ")");

            await db.Database.MigrateAsync();

            var templates = await db.Database
                .SqlQueryRaw<int>("SELECT DnsMode AS Value FROM Templates ORDER BY Id")
                .ToListAsync();
            var clients = await db.Database
                .SqlQueryRaw<string>("SELECT DnsMode || ':' || Dns AS Value FROM Clients")
                .ToListAsync();
            Assert.Equal([2, 0], templates);
            Assert.Equal(["0:"], clients);
        }
    }
}
