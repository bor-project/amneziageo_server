using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class ResolverHoldersTests : IDisposable
{
    private const string At = "'2026-10-20 10:00:00+00:00'";

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-resolver-holders-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Theory]
    [InlineData(1, 53, 3)]
    [InlineData(1, 5353, 0)]
    [InlineData(0, 53, 0)]
    public async Task TheClientsOfAResolverThatAnsweredBeforeTheUpdateHoldFilesThatNameIt(int enabled, int port, int holders)
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database}").Options);
        await using (db)
        {
            await db.GetService<IMigrator>().MigrateAsync("DropRuleSources");
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Resolver (CacheSize, IsEnabled, Listen, MaxTtl, MinTtl, NameMinutes, Outbound, Port, UpdatedUtc, Upstreams) "
                + "VALUES (1000, {0}, '', 3600, 60, 60, '', {1}, " + At + ", '1.1.1.1')",
                enabled,
                port);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Templates (Id, AllowedIps, CreatedUtc, Dns, Entries, Missed, Name, Presets, UpdatedUtc, StandIns) VALUES "
                + "(1, '', " + At + ", '9.9.9.9', '', '', 'own', '', " + At + ", 0), "
                + "(2, '', " + At + ", '9.9.9.9', '', '', 'names', '', " + At + ", 1), "
                + "(3, '', " + At + ", '', '', '', 'bare', '', " + At + ", 0)");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Clients (Address, ConfigId, CreatedUtc, DailyLimit, Inbound, IsEnabled, Name, Note, PresharedKey, PrivateKey, PublicKey, SubscriptionId, TemplateId, UpdatedUtc) VALUES "
                + "('10.8.0.2/32', 1, " + At + ", 0, 0, 1, 'anna', '', '', '', 'k1', '', NULL, " + At + "), "
                + "('10.8.0.3/32', 1, " + At + ", 0, 0, 1, 'guest', '', '', '', 'k2', '', 1, " + At + "), "
                + "('10.8.0.4/32', 1, " + At + ", 0, 0, 1, 'kiosk', '', '', '', 'k3', '', 2, " + At + "), "
                + "('10.8.0.5/32', 1, " + At + ", 0, 0, 1, 'tablet', '', '', '', 'k4', '', 3, " + At + ")");

            await db.Database.MigrateAsync();

            var held = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Clients WHERE ResolverHanded = 1")
                .ToListAsync();
            var mode = await db.Database.SqlQueryRaw<int>("SELECT IsEnabled AS Value FROM Resolver").ToListAsync();
            Assert.Equal([holders], held);
            Assert.Equal([1], mode);
        }
    }
}
