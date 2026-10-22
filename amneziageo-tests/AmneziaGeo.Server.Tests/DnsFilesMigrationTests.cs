using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class DnsFilesMigrationTests : IDisposable
{
    private const string At = "'2026-10-22 10:00:00+00:00'";

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-dns-files-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void TheModelMatchesTheMigrations()
    {
        using var db = Open();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task TheServerTakesTheChoiceOfTheEndpointWithMostClientsAndTheClientsOfTheOthersKeepTheirs()
    {
        await using var db = Open();
        await db.GetService<IMigrator>().MigrateAsync("DnsChoices");
        await PrepareAsync(db, resolver: true, first: 1, second: 0);

        await db.Database.MigrateAsync();

        Assert.Equal([1], await IntsAsync(db, "SELECT FilesMode AS Value FROM Resolver"));
        Assert.Equal([""], await TextsAsync(db, "SELECT FilesDns AS Value FROM Resolver"));
        Assert.Equal(
            ["anna:0", "boris:0", "carl:0", "dave:2", "erin:5", "fred:5", "gina:0"],
            await TextsAsync(db, "SELECT Name || ':' || DnsMode AS Value FROM Clients ORDER BY Name"));
        Assert.DoesNotContain("DnsMode", await TextsAsync(db, "SELECT name AS Value FROM pragma_table_info('Configs')"));
    }

    [Fact]
    public async Task AnEndpointWithServersOfItsOwnBecomesTheServersOfTheEndpointAtTheServerOrAtItsClients()
    {
        await using var db = Open();
        await db.GetService<IMigrator>().MigrateAsync("DnsChoices");
        await PrepareAsync(db, resolver: true, first: 2, second: 1);

        await db.Database.MigrateAsync();

        Assert.Equal([4], await IntsAsync(db, "SELECT FilesMode AS Value FROM Resolver"));
        Assert.Equal(
            ["anna:0", "boris:0", "carl:0", "dave:2", "erin:1", "fred:1", "gina:0"],
            await TextsAsync(db, "SELECT Name || ':' || DnsMode AS Value FROM Clients ORDER BY Name"));
    }

    [Fact]
    public async Task WithoutSettingsOfTheResolverTheServerStaysAutomaticAndTheClientsKeepTheChoiceOfTheirEndpoint()
    {
        await using var db = Open();
        await db.GetService<IMigrator>().MigrateAsync("DnsChoices");
        await PrepareAsync(db, resolver: false, first: 1, second: 0);

        await db.Database.MigrateAsync();

        Assert.Empty(await IntsAsync(db, "SELECT FilesMode AS Value FROM Resolver"));
        Assert.Equal(
            ["anna:1", "boris:1", "carl:0", "dave:2", "erin:0", "fred:0", "gina:0"],
            await TextsAsync(db, "SELECT Name || ':' || DnsMode AS Value FROM Clients ORDER BY Name"));
    }

    [Fact]
    public async Task EndpointsThatPickedTheSameLeaveTheClientsAsTheyWere()
    {
        await using var db = Open();
        await db.GetService<IMigrator>().MigrateAsync("DnsChoices");
        await PrepareAsync(db, resolver: true, first: 0, second: 0);

        await db.Database.MigrateAsync();

        Assert.Equal([5], await IntsAsync(db, "SELECT FilesMode AS Value FROM Resolver"));
        Assert.Equal(
            ["anna:0", "boris:0", "carl:0", "dave:2", "erin:0", "fred:0", "gina:0"],
            await TextsAsync(db, "SELECT Name || ':' || DnsMode AS Value FROM Clients ORDER BY Name"));
    }

    private AppDbContext Open() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={Path.Combine(_folder.FullName, "server.db")}").Options);

    // Two endpoints with the choices given, four clients at the first, three at the second: one with no template, one with
    // a template that leaves the choice above, one with a template of its own servers, one with its own servers.
    private static async Task PrepareAsync(AppDbContext db, bool resolver, int first, int second)
    {
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        if (resolver)
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Resolver (Id, CacheSize, IsEnabled, Listen, MaxTtl, MinTtl, NameMinutes, Outbound, Port, UpdatedUtc, Upstreams) "
                + "VALUES (1, 1000, 1, '', 3600, 60, 60, '', 53, " + At + ", 'https://1.1.1.1/dns-query')");
        }

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Configs (Id, Address, AllowedIps, Blocked, ContentPaddingAddition, CreatedUtc, DisableCookies, Dns, DnsMode, H1, H2, H3, H4, "
            + "HeaderProtectionKey, Host, Inbound, IsEnabled, Jc, Jmax, Jmin, Keepalive, KeepaliveTimeout, ListenPort, MaxHandshakeAttempts, "
            + "Mtu, Name, Nat, OfflineAfter, Opened, PresharedKey, PrivateKey, PublicKey, RandomTrailers, RejectAfterTime, RekeyAfterTime, "
            + "RekeyTimeout, S1, S2, S3, S4, ServicesPort, UpdatedUtc, WebSocket) VALUES "
            + "(1, '10.8.0.1/24', '0.0.0.0/0', '', '', " + At + ", 0, '1.1.1.1', {0}, '', '', '', '', '', '192.0.2.1', 0, 1, 0, 0, 0, 25, '', "
            + "51820, '', 1420, 'awg1', 0, 60, 0, '', '', 'p1', 0, '', '', '', 0, 0, 0, 0, 0, " + At + ", 0), "
            + "(2, '10.9.0.1/24', '0.0.0.0/0', '', '', " + At + ", 0, '8.8.8.8', {1}, '', '', '', '', '', '192.0.2.1', 0, 1, 0, 0, 0, 25, '', "
            + "51821, '', 1420, 'awg2', 0, 60, 0, '', '', 'p2', 0, '', '', '', 0, 0, 0, 0, 0, " + At + ", 0)",
            first,
            second);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Templates (Id, AllowedIps, CreatedUtc, Dns, DnsMode, Entries, Missed, Name, Presets, UpdatedUtc, StandIns) VALUES "
            + "(1, '', " + At + ", '', 0, '', '', 'above', '', " + At + ", 0), "
            + "(2, '', " + At + ", '9.9.9.9', 2, '', '', 'own', '', " + At + ", 0)");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Clients (Address, ConfigId, CreatedUtc, DailyLimit, Dns, DnsMode, Inbound, IsEnabled, Name, Note, PresharedKey, PrivateKey, "
            + "PublicKey, SubscriptionId, TemplateId, UpdatedUtc) VALUES "
            + "('10.8.0.2/32', 1, " + At + ", 0, '', 0, 0, 1, 'anna', '', '', '', 'k1', '', NULL, " + At + "), "
            + "('10.8.0.3/32', 1, " + At + ", 0, '', 0, 0, 1, 'boris', '', '', '', 'k2', '', 1, " + At + "), "
            + "('10.8.0.4/32', 1, " + At + ", 0, '', 0, 0, 1, 'carl', '', '', '', 'k3', '', 2, " + At + "), "
            + "('10.8.0.5/32', 1, " + At + ", 0, '8.8.4.4', 2, 0, 1, 'dave', '', '', '', 'k4', '', NULL, " + At + "), "
            + "('10.9.0.2/32', 2, " + At + ", 0, '', 0, 0, 1, 'erin', '', '', '', 'k5', '', NULL, " + At + "), "
            + "('10.9.0.3/32', 2, " + At + ", 0, '', 0, 0, 1, 'fred', '', '', '', 'k6', '', 1, " + At + "), "
            + "('10.9.0.4/32', 2, " + At + ", 0, '', 0, 0, 1, 'gina', '', '', '', 'k7', '', 2, " + At + ")");
    }

    private static Task<List<int>> IntsAsync(AppDbContext db, string sql) => db.Database.SqlQueryRaw<int>(sql).ToListAsync();

    private static Task<List<string>> TextsAsync(AppDbContext db, string sql) => db.Database.SqlQueryRaw<string>(sql).ToListAsync();
}
