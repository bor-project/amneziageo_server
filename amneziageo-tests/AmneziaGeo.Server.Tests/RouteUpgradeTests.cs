using System.Net;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Dal.Exchange;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace AmneziaGeo.Server.Tests;

public sealed class RouteUpgradeTests
{
    [Fact]
    public async Task AClientCannotBroadenAMigratedException()
    {
        using var bench = new Bench();
        await bench.Rules.SaveBasicAsync(new RouteBasic { Block = ["192.0.2.1"] }, CancellationToken.None).ConfigureAwait(false);
        var held = await bench.Rules.FindAsync(RouteBasic.BlockId, CancellationToken.None).ConfigureAwait(false);
        await bench.Rules.ChangeAsync(held!.Id, held with { Clients = ["poco"] }, CancellationToken.None).ConfigureAwait(false);

        var refusal = await bench.Rules.RefuseNamingAsync(RuleSide.Clients, "poco", [], CancellationToken.None).ConfigureAwait(false);
        var unchanged = await RuleNaming.NameAsync(bench.Rules, RuleSide.Clients, "poco", [RouteBasic.BlockId], CancellationToken.None).ConfigureAwait(false);
        var retained = await RuleNaming.AddedAsync(bench.Rules, RuleSide.Clients, "poco", [123], CancellationToken.None).ConfigureAwait(false);

        Assert.Equal("rule-keeps-last", refusal?.Code);
        Assert.False(unchanged);
        Assert.Contains(RouteBasic.BlockId, retained!);
        Assert.Equal(["poco"], (await bench.Rules.FindAsync(RouteBasic.BlockId, CancellationToken.None).ConfigureAwait(false))!.Clients);
    }

    [Fact]
    public async Task AnInterfaceCannotBroadenAMigratedException()
    {
        using var bench = new Bench();
        await bench.Rules.SaveBasicAsync(new RouteBasic { Direct = ["192.0.2.1"] }, CancellationToken.None).ConfigureAwait(false);
        var held = await bench.Rules.FindAsync(RouteBasic.DirectId, CancellationToken.None).ConfigureAwait(false);
        await bench.Rules.ChangeAsync(held!.Id, held with { Inbounds = ["awg1"] }, CancellationToken.None).ConfigureAwait(false);

        var refusal = await bench.Rules.RefuseNamingAsync(RuleSide.Inbounds, "awg1", [], CancellationToken.None).ConfigureAwait(false);

        Assert.Equal("rule-keeps-last", refusal?.Code);
    }

    [Fact]
    public async Task AMigratedExceptionKeepsItsDirection()
    {
        using var bench = new Bench();
        await bench.Rules.SaveBasicAsync(new RouteBasic { Direct = ["192.0.2.1"] }, CancellationToken.None).ConfigureAwait(false);
        var held = await bench.Rules.FindAsync(RouteBasic.DirectId, CancellationToken.None).ConfigureAwait(false);
        await bench.Rules.ChangeAsync(held!.Id, held with { Action = RouteAction.Out, Outbound = "away" }, CancellationToken.None).ConfigureAwait(false);

        var refusal = await bench.Rules.RefuseLeavingAsync("away", [], CancellationToken.None).ConfigureAwait(false);
        var unknown = await bench.Rules.RefuseLeavingAsync("away", [-42], CancellationToken.None).ConfigureAwait(false);

        Assert.Equal("way-keeps-rules", refusal?.Code);
        Assert.Equal("unknown-rule", unknown?.Code);
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var protocol in new[] { RouteProtocol.Tcp, RouteProtocol.Udp })
        {
            foreach (var ipv6 in new[] { false, true })
            {
                foreach (var alive in new[] { false, true })
                {
                    foreach (var dns in new[] { false, true })
                    {
                        yield return [protocol, ipv6, alive, dns];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task LegacyRulesKeepTheirDecisionsAndHostRules(string protocol, bool ipv6, bool alive, bool dns)
    {
        using var bench = new Bench();
        var rule = RouteDefaults.Fresh("kept") with
        {
            Outbound = "away",
            Clients = ["poco"],
            Inbounds = ["awg1"],
            Ports = ["443"],
            SourcePorts = ["5000-6000"],
            Protocol = protocol,
            Targets = ["198.51.100.0/24", "2001:db8::/32", "example.test"],
        };
        var added = await bench.Rules.AddAsync(rule, CancellationToken.None).ConfigureAwait(false);
        var disabled = await bench.Rules.AddAsync(RouteDefaults.Fresh("disabled") with
        {
            Action = RouteAction.Block,
            IsEnabled = false,
            Targets = ["0.0.0.0/0", "::/0"],
        }, CancellationToken.None).ConfigureAwait(false);
        Assert.True(added.IsOk, added.Message);
        Assert.True(disabled.IsOk, disabled.Message);
        await bench.Rules.SaveBasicAsync(new RouteBasic
        {
            Block = ["10.0.0.0/8", "fd00::/8"],
            Direct = ["198.51.100.7/32", "2001:db8:9::7/128"],
        }, default).ConfigureAwait(false);
        var beforeRules = await bench.Rules.ListAsync(CancellationToken.None).ConfigureAwait(false);
        var basic = await bench.Rules.ReadBasicAsync(CancellationToken.None).ConfigureAwait(false);
        var before = Plan(beforeRules, basic, protocol, alive, dns);

        await bench.Db.Database.MigrateAsync(CancellationToken.None).ConfigureAwait(false);
        await bench.Db.Database.MigrateAsync(CancellationToken.None).ConfigureAwait(false);

        var afterRules = await bench.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false);
        var after = Plan(afterRules, await bench.Rules.ReadBasicAsync(CancellationToken.None).ConfigureAwait(false), protocol, alive, dns);
        Assert.Equal([RouteBasic.BlockId, RouteBasic.DirectId, added.Record!.Id, disabled.Record!.Id], afterRules.Select(one => one.Id));
        Assert.Equal(RouteRuleset.Text(before), RouteRuleset.Text(after));
        Assert.Equal(RouteRuleset.Guarding(before), RouteRuleset.Guarding(after));
        Assert.Equal(2, (await bench.Rules.ListAsync(CancellationToken.None).ConfigureAwait(false)).Count);
        foreach (var destination in ipv6
            ? new[] { "fd00::7", "2001:db8:9::7", "2001:db8::5", "2001:4860:4860::8888" }
            : new[] { "10.2.3.4", "198.51.100.7", "198.51.100.5", "4.4.4.4" })
        {
            var query = new RouteQuery
            {
                Addresses = [IPAddress.Parse(destination)],
                Sources = [IPAddress.Parse("10.9.0.2")],
                Inbound = "awg1",
                Port = 443,
                SourcePort = 5500,
                Protocol = protocol,
            };
            var first = RouteProbe.Test(before, query, (_, _) => false);
            var second = RouteProbe.Test(after, query, (_, _) => false);
            Assert.Equal(first.Verdict, second.Verdict);
            Assert.Equal(first.Leg?.Rule.Id, second.Leg?.Rule.Id);
        }
    }

    [Fact]
    public async Task TheNewTableCanReorderExceptionsWhileLegacyMovesKeepTheirPlaces()
    {
        using var bench = new Bench();
        await bench.Rules.SaveBasicAsync(new RouteBasic { Block = ["10.0.0.0/8"], Direct = ["192.0.2.1"] }, CancellationToken.None).ConfigureAwait(false);
        var added = await bench.Rules.AddAsync(RouteDefaults.Fresh("kept") with { Action = RouteAction.Direct }, CancellationToken.None).ConfigureAwait(false);
        await bench.Rules.MoveAsync(added.Record!.Id, true, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(RouteBasic.BlockId, (await bench.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false))[0].Id);

        await bench.Rules.PlaceAsync(added.Record.Id, 1, CancellationToken.None, table: true).ConfigureAwait(false);

        Assert.Equal([added.Record.Id, RouteBasic.BlockId, RouteBasic.DirectId],
            (await bench.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false)).Select(one => one.Id));
        Assert.Equal(["10.0.0.0/8"], (await bench.Rules.ReadBasicAsync(CancellationToken.None).ConfigureAwait(false)).Block);
    }

    [Fact]
    public async Task AnUnchangedLegacyWriteKeepsNewConditionsAndDisabledState()
    {
        using var bench = new Bench();
        await bench.Rules.SaveBasicAsync(new RouteBasic { Block = ["10.0.0.0/8"] }, CancellationToken.None).ConfigureAwait(false);
        var held = await bench.Rules.FindAsync(RouteBasic.BlockId, CancellationToken.None).ConfigureAwait(false);
        var changed = await bench.Rules.ChangeAsync(RouteBasic.BlockId, held! with
        {
            IsEnabled = false,
            Clients = ["poco"],
            Ports = ["443"],
            SourcePorts = ["5000"],
        }, CancellationToken.None).ConfigureAwait(false);
        Assert.True(changed.IsOk, changed.Message);

        await bench.Rules.SaveBasicAsync(new RouteBasic { Block = ["10.0.0.0/8"], Direct = ["192.0.2.1"] }, CancellationToken.None).ConfigureAwait(false);
        var after = await bench.Rules.FindAsync(RouteBasic.BlockId, CancellationToken.None).ConfigureAwait(false);

        Assert.False(after!.IsEnabled);
        Assert.Equal(["poco"], after.Clients);
        Assert.Equal(["443"], after.Ports);
        Assert.Equal(["5000"], after.SourcePorts);
        Assert.Equal(changed.Record!.Position, after.Position);
    }

    [Fact]
    public async Task ALongExceptionIsStoredAsAReusableRule()
    {
        using var bench = new Bench();
        var basic = new RouteBasic { Block = [.. Enumerable.Range(1, 100).Select(index => "192.0.2." + index)] };
        Assert.Null(await bench.Rules.SaveBasicAsync(basic, default).ConfigureAwait(false));
        var after = Plan(await bench.Rules.TableAsync(default).ConfigureAwait(false), RouteBasic.Empty, RouteProtocol.Tcp, true, false);
        Assert.Null(Assert.Single(after.Legs).Fault);
        Assert.Equal(100, (await bench.Rules.FindAsync(RouteBasic.BlockId, default).ConfigureAwait(false))!.Targets.Count);
    }

    [Fact]
    public async Task UpdatingTheApplicationBacksUpMigratesAndChecksTheOldDatabase()
    {
        var folder = Directory.CreateTempSubdirectory("ag745-upgrade-");
        try
        {
            var path = Path.Combine(folder.FullName, "server.db");
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddServerDatabase(path, new AuthOptions(), Path.Combine(folder.FullName, "geo"));
            using var services = collection.BuildServiceProvider();
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var migrations = db.Database.GetMigrations().ToArray();
                var previous = migrations.TakeWhile(name => !name.EndsWith("_RoutingTable", StringComparison.Ordinal)).Last();
                await db.GetService<IMigrator>().MigrateAsync(previous).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO RouteBasics (Id, Direct, Block, UpdatedUtc) VALUES (1, '192.0.2.1', '10.0.0.0/8', '2026-11-09 12:00:00+00:00')").ConfigureAwait(false);
            }

            await ServerDatabase.PrepareAsync(services).ConfigureAwait(false);

            var backup = Assert.Single(folder.GetFiles("backup-*.db"));
            await ServerDatabase.PrepareAsync(services).ConfigureAwait(false);
            Assert.Single(folder.GetFiles("backup-*.db"));
            using var afterScope = services.CreateScope();
            var after = afterScope.ServiceProvider.GetRequiredService<RouteStore>();
            Assert.Equal([RouteBasic.BlockId, RouteBasic.DirectId],
                (await after.TableAsync(CancellationToken.None).ConfigureAwait(false)).Where(rule => rule.Id < 0).Select(rule => rule.Id));
            using var saved = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=" + backup.FullName).Options);
            Assert.Equal("192.0.2.1", (await saved.Database.SqlQueryRaw<string>("SELECT Direct AS Value FROM RouteBasics WHERE Id = 1").ToListAsync().ConfigureAwait(false)).Single());
            Assert.False((await saved.Database.GetAppliedMigrationsAsync().ConfigureAwait(false)).Any(name => name.EndsWith("_RoutingTable", StringComparison.Ordinal)));
        }
        finally
        {
            Pools.ClearUnder(folder.FullName);
            folder.Delete(recursive: true);
        }
    }

    private static RoutePlan Plan(IReadOnlyList<RouteRule> rules, RouteBasic basic, string protocol, bool alive, bool dns) =>
        RoutePlan.Build(rules,
            [new OutboundConfig { Name = "away", Kind = OutboundKind.Wg, Mark = 0xA601, Table = 42601 }],
            GeoIndex.Load([], new MemoryGeoFiles()), ["awg1"],
            DnsDefaults.Settings with { IsEnabled = dns },
            alive: alive ? new HashSet<string> { "away" } : [],
            clients: [new TunnelClient { Id = 1, ConfigId = 1, Name = "poco", Address = ["10.9.0.2/32"] }],
            basic: basic);
}
