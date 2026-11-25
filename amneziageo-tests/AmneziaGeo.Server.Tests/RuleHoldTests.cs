using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class RuleHoldTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-rule-hold-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task ARuleSavedToLetItsTrafficOutHoldsItAfterTheUpdate()
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database}").Options);
        await using (db)
        {
            await db.GetService<IMigrator>().MigrateAsync("DropDnsSwitches");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Rules (Name, Position, IsEnabled, Action, Outbound, HoldsWhenDown, Targets, Sources, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc) VALUES "
                + "('abroad', 1, 1, 'out', 'gone', 0, '77.88.8.0/24', '', '', '', '', '', 'any', '2026-10-19 10:00:00+00:00', '2026-10-19 10:00:00+00:00')");

            await db.Database.MigrateAsync();

            var hold = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('RoutingEntries') WHERE name = 'HoldsWhenDown'")
                .ToListAsync();
            var rules = await new RouteStore(db).ListAsync(CancellationToken.None).ConfigureAwait(false);
            var rule = Assert.Single(rules, one => one.Name == "abroad");
            var fallback = Assert.Single(rules, one => one.Action == RouteAction.Direct);
            Assert.True(fallback.Position > rule.Position);
            var plan = RoutePlan.Build(rules, [], GeoIndex.Load([], new MemoryGeoFiles()), ["awg1"]);
            var leg = Assert.Single(plan.Legs, one => one.Rule.Id == rule.Id);
            Assert.Equal([0], hold);
            Assert.Equal("unknown-outbound", leg.Fault?.Code);
            Assert.True(leg.IsHeld);
            Assert.Contains("ip daddr @r1v4 drop", RouteRuleset.Text(plan), StringComparison.Ordinal);
        }
    }
}
