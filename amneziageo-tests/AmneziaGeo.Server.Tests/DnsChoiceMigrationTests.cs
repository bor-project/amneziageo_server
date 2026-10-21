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

    [Theory]
    [InlineData(1, 53, "", 1, "", 0, 1)]
    [InlineData(1, 53, "youtube.com", 1, "", 0, 0)]
    [InlineData(1, 53, "domain:example.org", 1, "", 0, 0)]
    [InlineData(1, 53, "geosite:ru", 1, "", 0, 0)]
    [InlineData(1, 53, "keyword:video", 1, "", 0, 0)]
    [InlineData(1, 53, "youtube.com", 0, "", 0, 1)]
    [InlineData(1, 53, "geoip:ru, cidr:198.51.100.0/24, 203.0.113.0/24, 2001:db8::/32", 1, "", 0, 1)]
    [InlineData(1, 53, "", 1, "192.0.2.0/24, example.net", 0, 0)]
    [InlineData(1, 53, "", 1, "", 1, 0)]
    [InlineData(1, 5353, "", 1, "", 0, 0)]
    [InlineData(0, 53, "", 1, "", 0, 0)]
    public async Task AnEndpointPicksTheResolverThatAnsweredBeforeTheUpdateWhileNoRoutingNeededIt(
        int enabled,
        int port,
        string targets,
        int ruleOn,
        string direct,
        int standIns,
        int picked)
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
                "INSERT INTO Configs (Id, Address, AllowedIps, Blocked, ContentPaddingAddition, CreatedUtc, DisableCookies, Dns, H1, H2, H3, H4, "
                + "HeaderProtectionKey, Host, Inbound, IsEnabled, Jc, Jmax, Jmin, Keepalive, KeepaliveTimeout, ListenPort, MaxHandshakeAttempts, "
                + "Mtu, Name, Nat, OfflineAfter, Opened, PresharedKey, PrivateKey, PublicKey, RandomTrailers, RejectAfterTime, RekeyAfterTime, "
                + "RekeyTimeout, S1, S2, S3, S4, ServicesPort, UpdatedUtc, WebSocket) VALUES "
                + "(1, '10.8.0.1/24', '0.0.0.0/0', '', '', " + At + ", 0, '1.1.1.1', '', '', '', '', '', '192.0.2.1', 0, 1, 0, 0, 0, 25, '', "
                + "51820, '', 1420, 'awg1', 0, 60, 0, '', '', 'p1', 0, '', '', '', 0, 0, 0, 0, 0, " + At + ", 0), "
                + "(2, '10.9.0.1/24', '0.0.0.0/0', '', '', " + At + ", 0, '8.8.8.8', '', '', '', '', '', '192.0.2.1', 0, 1, 0, 0, 0, 25, '', "
                + "51821, '', 1420, 'awg2', 0, 60, 0, '', '', 'p2', 0, '', '', '', 0, 0, 0, 0, 0, " + At + ", 0)");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Templates (Id, AllowedIps, CreatedUtc, Dns, Entries, Missed, Name, Presets, UpdatedUtc, StandIns) VALUES "
                + "(1, '', " + At + ", '', 'example.com', '', 'cdn', '', " + At + ", {0})",
                standIns);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO RouteBasics (Id, Direct, Block, UpdatedUtc) VALUES (1, {0}, '', " + At + ")",
                direct);
            if (targets.Length > 0)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Rules (Action, Clients, CreatedUtc, Inbounds, IsEnabled, Name, Outbound, Ports, Position, Protocol, SourcePorts, Targets, UpdatedUtc) "
                    + "VALUES ('out', '', " + At + ", '', {0}, 'yt', 'finland', '', 1, '', '', {1}, " + At + ")",
                    ruleOn,
                    targets);
            }

            await db.Database.MigrateAsync();

            var endpoints = await db.Database
                .SqlQueryRaw<int>("SELECT DnsMode AS Value FROM Configs ORDER BY Id")
                .ToListAsync();
            var mode = await db.Database.SqlQueryRaw<int>("SELECT IsEnabled AS Value FROM Resolver").ToListAsync();
            Assert.Equal([picked, picked], endpoints);
            Assert.Equal([1], mode);
        }
    }
}
