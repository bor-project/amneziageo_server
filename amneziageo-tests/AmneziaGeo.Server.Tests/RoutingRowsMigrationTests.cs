using System.Net;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed partial class RoutingModelMigrationTests
{
    private const string RoutingRowsVersion = "20261127153000_RoutingRows";
    private const string RowFallbackKey = "route-fallback";

    [Theory]
    [InlineData(Stable)]
    [InlineData(Beta)]
    public async Task RoutingRowsKeepReleasedDatabasesAndTheirRoutingDecisions(string version)
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(version, "routing").ConfigureAwait(false);
        var protectedBefore = await fixture.ProtectedAsync().ConfigureAwait(false);
        await fixture.MigrateRoutingModelAsync().ConfigureAwait(false);
        var store = new RouteStore(fixture.Db);
        var before = await store.TableAsync(default).ConfigureAwait(false);
        var oldPlan = await RowsPlanAsync(fixture, before).ConfigureAwait(false);

        await fixture.Db.GetService<IMigrator>().MigrateAsync(RoutingRowsVersion).ConfigureAwait(false);
        fixture.Db.ChangeTracker.Clear();
        var after = await store.TableAsync(default).ConfigureAwait(false);
        var newPlan = await RowsPlanAsync(fixture, after).ConfigureAwait(false);
        AssertRows(before, after);
        Assert.Equal(protectedBefore.Digest, (await fixture.ProtectedAsync(protectedBefore.Columns).ConfigureAwait(false)).Digest);
        AssertDecisions(oldPlan, newPlan);

        var once = await fixture.AllAsync().ConfigureAwait(false);
        await fixture.Db.GetService<IMigrator>().MigrateAsync(RoutingRowsVersion).ConfigureAwait(false);
        Assert.Equal(once, await fixture.AllAsync().ConfigureAwait(false));
        await fixture.IntegrityAsync().ConfigureAwait(false);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public async Task EmptyCriteriaKeepTheirPlaceAndDnsWhenASharedLastFallbackIsAdopted(int emptyCriterion)
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.MigrateRoutingModelAsync().ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE TrafficRules SET Targets = '' WHERE Id = " + emptyCriterion).ConfigureAwait(false);
        await fixture.SqlAsync("""
            UPDATE TrafficRules
            SET DnsMode = 'custom', Dns = 'tls://1.0.0.1', DnsMembers = '{{"edge":["https://9.9.9.9/dns-query"],"second":["8.8.4.4"]}}'
            WHERE Id = 11;
            UPDATE RoutingEntries
            SET Action = 'out', Outbound = 'pool'
            WHERE Id = (SELECT EntryId FROM RoutingEntryRules WHERE RuleId = (SELECT Id FROM TrafficRules WHERE DefaultKey = 'resolver-default'));
            INSERT INTO RoutingEntries (Id, Name, Position, IsEnabled, Action, Outbound, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc)
            SELECT 9301, 'video-route (41:2)', 350, 0, Action, Outbound, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc
            FROM RoutingEntries ORDER BY Position DESC, Id DESC LIMIT 1;
            INSERT INTO RoutingEntryRules (EntryId, RuleId, Position)
            SELECT 9301, Id, 0 FROM TrafficRules WHERE DefaultKey = 'resolver-default';
            UPDATE sqlite_sequence SET seq = 9500 WHERE name = 'RoutingEntries';
            """).ConfigureAwait(false);
        var store = new RouteStore(fixture.Db);
        var before = await store.TableAsync(default).ConfigureAwait(false);
        var oldPlan = await RowsPlanAsync(fixture, before).ConfigureAwait(false);

        await fixture.Db.GetService<IMigrator>().MigrateAsync(RoutingRowsVersion).ConfigureAwait(false);
        fixture.Db.ChangeTracker.Clear();
        var after = await store.TableAsync(default).ConfigureAwait(false);
        var newPlan = await RowsPlanAsync(fixture, after).ConfigureAwait(false);
        Assert.Equal(before[^1].Id, after[^1].Id);
        Assert.Equal(int.MaxValue, after[^1].Position);
        Assert.Equal("out", after[^1].Action);
        Assert.Equal("pool", after[^1].Outbound);
        Assert.NotEqual(Assert.Single(before[^1].Criteria).Id, Assert.Single(after[^1].Criteria).Id);
        var shared = after.Single(row => row.Id == 9301);
        Assert.False(shared.IsEnabled);
        Assert.Equal(Assert.Single(before[^1].Criteria).Id, Assert.Single(shared.Criteria).Id);
        Assert.Equal("resolver-default", Assert.Single(shared.Criteria).DefaultKey);
        Assert.Equal(before[^1].Criteria[0].Dns, after[^1].Criteria[0].Dns);

        var first = after.ToList().FindIndex(row => row.Id == 41);
        Assert.Equal(["video-route", "video-route (41:2:1)"], after.Skip(first).Take(2).Select(row => row.Name));
        Assert.True(after[first + 1].Id > 9500);
        Assert.Equal(emptyCriterion, Assert.Single(after.Skip(first).Take(3), row => !row.HasTargets).Criteria[0].Id);
        AssertDecisions(oldPlan, newPlan);
        await fixture.IntegrityAsync().ConfigureAwait(false);
    }

    [Theory]
    [InlineData("Position = 50")]
    [InlineData("IsEnabled = 0")]
    [InlineData("Clients = 'canary'")]
    public async Task AFormerResolverRuleIsNotPromotedWhenItsPositionOrConditionsAreUserDefined(string change)
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.MigrateRoutingModelAsync().ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE RoutingEntries SET " + change +
            " WHERE Id = (SELECT EntryId FROM RoutingEntryRules WHERE RuleId = (SELECT Id FROM TrafficRules WHERE DefaultKey = 'resolver-default'))").ConfigureAwait(false);
        var store = new RouteStore(fixture.Db);
        var before = await store.TableAsync(default).ConfigureAwait(false);
        var old = Assert.Single(before, row => row.Criteria.Any(criterion => criterion.DefaultKey == "resolver-default"));

        await fixture.Db.GetService<IMigrator>().MigrateAsync(RoutingRowsVersion).ConfigureAwait(false);
        fixture.Db.ChangeTracker.Clear();
        var after = await store.TableAsync(default).ConfigureAwait(false);
        var kept = Assert.Single(after, row => row.Id == old.Id);
        Assert.Equal(old.IsEnabled, kept.IsEnabled);
        Assert.Equal(old.Clients, kept.Clients);
        Assert.Equal(old.Criteria[0].Dns, kept.Criteria[0].Dns);
        Assert.Equal("resolver-default", kept.Criteria[0].DefaultKey);
        Assert.NotEqual(old.Id, after[^1].Id);
        Assert.Equal(int.MaxValue, after[^1].Position);
        Assert.True(after[^1].IsEnabled);
        Assert.Equal("direct", after[^1].Action);
        Assert.Equal(TrafficDnsMode.Default, Assert.Single(after[^1].Criteria).DnsMode);
        AssertRows(before, after, addedFallback: true);
        await fixture.IntegrityAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task AResolvedAddressStaysRoutedWhenTheFirstPartHasOnlyIpRanges()
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.MigrateRoutingModelAsync().ConfigureAwait(false);
        await fixture.SqlAsync("""
            UPDATE TrafficRules SET Targets = '198.51.100.0/24' WHERE Id = 11;
            INSERT INTO Standings (RuleId, Address, SeenUtc)
            VALUES (41, '203.0.113.81', '2026-11-26 22:00:00+00:00'),
                   (41, '2001:db8:1::81', '2026-11-26 22:01:00+00:00'),
                   (42, '203.0.113.82', '2026-11-26 22:02:00+00:00');
            """).ConfigureAwait(false);
        var routes = new RouteStore(fixture.Db);
        var standing = new DnsStandingStore(fixture.Db);
        var oldRows = await routes.TableAsync(default).ConfigureAwait(false);
        var oldPlan = await RowsPlanAsync(fixture, oldRows).ConfigureAwait(false);
        var oldAddresses = await standing.ListAsync(default).ConfigureAwait(false);
        var oldSets = new DnsSets(new Ledger());
        oldSets.Restore(oldAddresses, TimeSpan.FromMinutes(30));

        await fixture.Db.GetService<IMigrator>().MigrateAsync(RoutingRowsVersion).ConfigureAwait(false);
        fixture.Db.ChangeTracker.Clear();
        var rows = await routes.TableAsync(default).ConfigureAwait(false);
        var plan = await RowsPlanAsync(fixture, rows).ConfigureAwait(false);
        var addresses = await standing.ListAsync(default).ConfigureAwait(false);
        var sets = new DnsSets(new Ledger());
        sets.Restore(addresses, TimeSpan.FromMinutes(30));

        var first = rows.ToList().FindIndex(row => row.Id == 41);
        Assert.Empty(plan.Legs[first].Domains);
        Assert.Equal(41L, rows[first].Id);
        Assert.Single(addresses, item => item.Rule == 42);
        Assert.Equal(oldRows.Single(row => row.Id == 41).Criteria.Count * 2 + 1, addresses.Count);
        foreach (var held in oldAddresses.Where(item => item.Rule == 41))
        {
            var query = new RouteQuery { Addresses = [held.Address] };
            var oldChoice = RouteProbe.Test(oldPlan, query, oldSets.Holds);
            var newChoice = RouteProbe.Test(plan, query, sets.Holds);
            Assert.Equal(41L, oldChoice.Leg!.Rule.Id);
            Assert.Equal(rows[first + 1].Id, newChoice.Leg!.Rule.Id);
            Assert.Equal(oldChoice.Verdict, newChoice.Verdict);
            Assert.Equal(oldChoice.Leg.Rule.Outbound, newChoice.Leg.Rule.Outbound);
            Assert.Contains(held, addresses);
            Assert.Contains(held with { Rule = newChoice.Leg.Rule.Id }, sets.Standings(plan));
        }
        await fixture.IntegrityAsync().ConfigureAwait(false);
    }

    private static void AssertRows(IReadOnlyList<RouteRule> before, IReadOnlyList<RouteRule> after, bool addedFallback = false)
    {
        var position = 0;
        foreach (var prior in before)
        {
            Assert.Equal(prior.Id, after[position].Id);
            Assert.Equal(prior.Name, after[position].Name);
            foreach (var criterion in prior.Criteria)
            {
                var row = after[position++];
                var now = Assert.Single(row.Criteria);
                Assert.Equal(criterion.Id, now.Id);
                Assert.Equal(criterion.Targets, now.Targets);
                Assert.Equal(criterion.Dns, now.Dns);
                Assert.Equal(criterion.DnsMode, now.DnsMode);
                Assert.Equal(criterion.DnsMembers.Keys.Order(), now.DnsMembers.Keys.Order());
                foreach (var pair in criterion.DnsMembers)
                {
                    Assert.Equal(pair.Value, now.DnsMembers[pair.Key]);
                }

                Assert.Equal(prior.Action, row.Action);
                Assert.Equal(prior.Outbound, row.Outbound);
                Assert.Equal(prior.IsEnabled, row.IsEnabled);
                Assert.Equal(prior.Clients, row.Clients);
                Assert.Equal(prior.Inbounds, row.Inbounds);
                Assert.Equal(prior.Ports, row.Ports);
                Assert.Equal(prior.SourcePorts, row.SourcePorts);
                Assert.Equal(prior.Protocol, row.Protocol);
            }
        }
        Assert.Equal(after.Count, position + (addedFallback ? 1 : 0));
        Assert.Equal(after[^1], Assert.Single(after, row => row.Criteria.Any(criterion => criterion.DefaultKey == RowFallbackKey)));
        Assert.Equal(after.Count, after.Select(row => row.Position).Distinct().Count());
    }

    private static async Task<RoutePlan> RowsPlanAsync(PriorDatabase fixture, IReadOnlyList<RouteRule> rows)
    {
        var ways = await new OutboundStore(fixture.Db).ListAsync(default).ConfigureAwait(false);
        var balancers = await new BalanceStore(fixture.Db).ListAsync(default).ConfigureAwait(false);
        var clients = new[] { new TunnelClient { Id = 9002, ConfigId = 9001, Name = "canary", Address = ["10.7.0.2/32", "fd77::2/128"] } };
        return RoutePlan.Build(rows, ways, GeoIndex.Load([], new MemoryGeoFiles()), ["wg-canary"], balancers: balancers, clients: clients);
    }

    private static void AssertDecisions(RoutePlan before, RoutePlan after)
    {
        foreach (var name in new[] { "shared.example", "video.example", "ads.example", "base.example", "blocked.example", "absent.example" })
        {
            var oldDns = DnsRouting.Select(before, name);
            var newDns = DnsRouting.Select(after, name);
            Assert.Equal(oldDns.Servers, newDns.Servers);
            Assert.Equal(oldDns.Way.Outbound, newDns.Way.Outbound);
            Assert.Equal(oldDns.Way.Mark, newDns.Way.Mark);
            Assert.Equal(oldDns.Way.Fault?.Code, newDns.Way.Fault?.Code);
            AssertRoute(before, after, new RouteQuery { Name = name });
        }
        foreach (var pair in new[] { ("192.0.2.7", "10.7.0.2"), ("2001:db8::7", "fd77::2") })
        {
            var query = new RouteQuery { Addresses = [IPAddress.Parse(pair.Item1)], Sources = [IPAddress.Parse(pair.Item2)], Inbound = "wg-canary", Port = 443, SourcePort = 50001, Protocol = "tcp" };
            AssertRoute(before, after, query);
            AssertRoute(before, after, query with { Port = 80 });
            AssertRoute(before, after, query with { SourcePort = 80 });
            AssertRoute(before, after, query with { Protocol = "udp" });
            AssertRoute(before, after, query with { Inbound = "other" });
        }
    }

    private static void AssertRoute(RoutePlan before, RoutePlan after, RouteQuery query)
    {
        var earlier = RouteProbe.Test(before, query, (_, _) => false);
        var later = RouteProbe.Test(after, query, (_, _) => false);
        Assert.Equal(earlier.Verdict, later.Verdict);
        Assert.Equal(earlier.Leg?.Rule.Action, later.Leg?.Rule.Action);
        Assert.Equal(earlier.Leg?.Rule.Outbound, later.Leg?.Rule.Outbound);
    }
}
