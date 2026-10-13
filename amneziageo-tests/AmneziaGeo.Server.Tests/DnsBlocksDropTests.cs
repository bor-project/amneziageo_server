using System.Text.Json;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class DnsBlocksDropTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "direct", Kind = OutboundKind.Local, Mark = 0xA601, Table = OutboundRules.MainTable },
    ];

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-dns-blocks-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task AResolverSavedWithTheSwitchesIsKeptWithoutThem()
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

            var switches = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('Resolver') WHERE name IN ('BlockDot', 'BlockDoh')")
                .ToListAsync();
            var held = await new DnsStore(db).ReadAsync(CancellationToken.None);
            Assert.Equal([0], switches);
            Assert.True(held.IsEnabled);
            Assert.True(held.Intercept);
            Assert.Equal(5300, held.Port);
            Assert.Equal(["https://9.9.9.9/dns-query", "1.1.1.1"], held.Upstreams);
            Assert.Equal("bor", held.Outbound);
            Assert.Equal(["10.8.0.1"], held.Listen);
            Assert.Equal((30, 128, 30, 600), (held.NameMinutes, held.CacheSize, held.MinTtl, held.MaxTtl));
        }
    }

    [Fact]
    public void WhatAPageOfAnEarlierPanelSavesWithTheSwitchesLaysNothing()
    {
        var request = JsonSerializer.Deserialize<DnsRequest>(
            """
            {"isEnabled": true, "port": 53, "upstreams": ["https://1.1.1.1/dns-query"], "outbound": "", "listen": [],
             "nameMinutes": 60, "cacheSize": 4096, "minTtl": 60, "maxTtl": 3600, "intercept": true,
             "blockDot": true, "blockDoh": true}
            """,
            Web)!;
        var rule = RouteDefaults.Fresh("rule") with { Id = 1, Action = RouteAction.Out, Outbound = "direct", Targets = ["youtube.com"] };

        var settings = DnsAnswers.Draft(request);
        var plan = RoutePlan.Build([rule], Ways, GeoIndex.Load([], new MemoryGeoFiles()), ["awg1"], settings);
        var text = RouteRuleset.Text(plan);
        var body = JsonSerializer.Serialize(DnsAnswers.Resolver(settings, new DnsState(), new DnsSets(new Ledger()), plan), Web);

        Assert.True(settings.IsEnabled);
        Assert.Contains("chain resolve", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dport 853", text, StringComparison.Ordinal);
        Assert.DoesNotContain("doh", text, StringComparison.Ordinal);
        Assert.Contains("\"intercept\":true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("block", body, StringComparison.OrdinalIgnoreCase);
    }
}
