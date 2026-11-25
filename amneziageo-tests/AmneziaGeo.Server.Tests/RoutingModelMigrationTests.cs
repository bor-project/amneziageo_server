using System.Data.Common;
using System.Net;
using AmneziaGeo.Server.Awg.Client;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class RoutingModelMigrationTests
{
    private const string Stable = "20261030101002_ConfigRequireUpdate";
    private const string Beta = "20261124140354_PendingInterfaceChanges";

    [Theory]
    [InlineData(Stable, "global")]
    [InlineData(Beta, "global")]
    [InlineData(Beta, "outbound")]
    [InlineData(Beta, "routing")]
    public async Task ReleasedDatabasesPreserveClientsAndRoutingWhileMovingDnsIntoRules(string version, string mode)
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(version, mode).ConfigureAwait(false);
        var before = await fixture.ProtectedAsync().ConfigureAwait(false);

        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);
        var after = await fixture.ProtectedAsync(before.Columns).ConfigureAwait(false);
        Assert.Equal(before.Digest, after.Digest);
        Assert.False(fixture.Db.Database.HasPendingModelChanges());
        await fixture.IntegrityAsync().ConfigureAwait(false);
        await fixture.ObsoleteAsync().ConfigureAwait(false);
        var store = new RouteStore(fixture.Db);
        var rows = await store.TableAsync(default).ConfigureAwait(false);
        var row = Assert.Single(rows, one => one.Id == 41);
        Assert.Equal("video-route", row.Name);
        Assert.Equal(100, row.Position);
        Assert.Equal("pool", row.Outbound);
        Assert.Equal(new[] { "domain:base.example", "domain:video.example", "domain:shared.example", "domain:ads.example" }.Order(), row.Targets.Order());
        Assert.Equal(version == Beta ? 3 : 1, row.RuleIds.Count);
        if (version == Beta)
        {
            Assert.Equal(new long[] { 11, 12 }, row.RuleIds.Take(2));
            Assert.Equal(["domain:base.example"], row.Criteria[^1].Targets);
        }
        Assert.All(row.Criteria, criterion => Assert.Equal(TrafficDnsMode.Custom, criterion.DnsMode));
        var limited = Assert.Single(rows, one => one.Id == 42);
        Assert.Equal(["canary"], limited.Clients);
        Assert.Equal(["wg-canary"], limited.Inbounds);
        Assert.Equal(["443"], limited.Ports);
        Assert.Equal(["50000-50010"], limited.SourcePorts);
        Assert.Equal("tcp", limited.Protocol);
        Assert.Equal(["192.0.2.0/24", "2001:db8::/32"], limited.Targets);
        Assert.Equal("direct", limited.Action);
        Assert.Equal("block", Assert.Single(rows, one => one.Id == 43).Action);
        Assert.Equal(["domain:legacy-direct.example"], Assert.Single(rows, one => one.Id == -2).Targets);
        Assert.Equal(["domain:legacy-block.example"], Assert.Single(rows, one => one.Id == -1).Targets);
        if (version == Beta)
        {
            var group = await new TrafficRuleStore(fixture.Db).FindAsync(11, default).ConfigureAwait(false);
            Assert.Equal("video", group!.Name);
            Assert.Equal(["domain:video.example", "domain:shared.example"], group.Targets);
            Assert.Equal(mode == "routing" ? ["1.1.1.1"] : mode == "outbound" ? ["8.8.8.8"] : new[] { "tls://1.0.0.1" }, group.Dns);
        }

        var ways = await new OutboundStore(fixture.Db).ListAsync(default).ConfigureAwait(false);
        Assert.Equal(["9.9.9.9"], ways.Single(way => way.Name == "edge").Dns);
        Assert.Equal(["8.8.8.8"], ways.Single(way => way.Name == "second").Dns);
        var balancers = await new BalanceStore(fixture.Db).ListAsync(default).ConfigureAwait(false);
        var clients = new[] { new TunnelClient { Id = 9002, ConfigId = 9001, Name = "canary", Address = ["10.7.0.2/32", "fd77::2/128"] } };
        var plan = RoutePlan.Build(rows, ways, GeoIndex.Load([], new MemoryGeoFiles()), ["wg-canary"], balancers: balancers, clients: clients);
        foreach (var pair in new[] { ("192.0.2.7", "10.7.0.2"), ("2001:db8::7", "fd77::2") })
        {
            var query = new RouteQuery { Addresses = [IPAddress.Parse(pair.Item1)], Sources = [IPAddress.Parse(pair.Item2)], Inbound = "wg-canary", Port = 443, SourcePort = 50001, Protocol = "tcp" };
            var selected = RouteProbe.Test(plan, query, (_, _) => false);
            Assert.Equal(42L, selected.Leg!.Rule.Id);
            Assert.Equal(RouteProbe.Host, selected.Verdict);
            Assert.NotEqual(42L, RouteProbe.Test(plan, query with { Inbound = "other" }, (_, _) => false).Leg?.Rule.Id);
            Assert.NotEqual(42L, RouteProbe.Test(plan, query with { Protocol = "udp" }, (_, _) => false).Leg?.Rule.Id);
            Assert.NotEqual(42L, RouteProbe.Test(plan, query with { Port = 80 }, (_, _) => false).Leg?.Rule.Id);
        }
        Assert.Equal(RouteProbe.Block, RouteProbe.Test(plan, new RouteQuery { Name = "blocked.example" }, (_, _) => false).Verdict);
        Assert.Equal(41L, RouteProbe.Test(plan, new RouteQuery { Name = "base.example" }, (_, _) => false).Leg?.Rule.Id);
        var picked = DnsRouting.Select(plan, "shared.example");
        Assert.Null(picked.Way.Fault);
        Assert.Equal("edge", picked.Way.Outbound);
        Assert.Equal(mode == "routing" ? ["1.1.1.1"] : mode == "outbound" ? ["8.8.8.8"] : new[] { "tls://1.0.0.1" }, picked.Servers);
        var general = DnsRouting.Select(plan, "base.example");
        Assert.Null(general.Way.Fault);
        Assert.Equal(mode == "routing" ? ["9.9.9.9"] : mode == "outbound" ? ["8.8.8.8"] : new[] { "tls://1.0.0.1" }, general.Servers);
        Assert.Equal("dns-blocked", DnsRouting.Select(plan, "blocked.example").Way.Fault?.Code);
        var held = RoutePlan.Build(rows, ways, GeoIndex.Load([], new MemoryGeoFiles()), ["wg-canary"], balancers: balancers, alive: new HashSet<string>());
        Assert.NotNull(DnsRouting.Select(held, "base.example").Way.Fault);
        Assert.Empty(DnsRouting.Select(held, "base.example").Servers);

        var once = await fixture.AllAsync().ConfigureAwait(false);
        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(once, await fixture.AllAsync().ConfigureAwait(false));
        Assert.Equal(before.Digest, (await fixture.ProtectedAsync(before.Columns).ConfigureAwait(false)).Digest);
    }

    [Fact]
    public async Task ANewInstallationHasADnsRuleAndNewEntriesPrecedeIt()
    {
        using var fixture = new PriorDatabase();
        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);
        var store = new RouteStore(fixture.Db);
        var fallback = Assert.Single(await store.TableAsync(default).ConfigureAwait(false));
        Assert.Equal("direct", fallback.Action);
        var criterionDefault = Assert.Single(fallback.Criteria);
        Assert.Equal(TrafficDnsMode.Default, criterionDefault.DnsMode);
        Assert.Empty(criterionDefault.Dns);
        var freshPlan = RoutePlan.Build([fallback], [], GeoIndex.Load([], new MemoryGeoFiles()), []) with { HostDns = ["192.0.2.53"] };
        Assert.Equal(["192.0.2.53"], DnsRouting.Select(freshPlan, "fresh.example").Servers);
        var missing = DnsRouting.Select(freshPlan with { HostDns = [] }, "fresh.example");
        Assert.Empty(missing.Servers);
        Assert.Equal("no-default-dns", missing.Way.Fault?.Code);
        var criterion = await new TrafficRuleStore(fixture.Db).AddAsync(new TrafficRule { Name = "video", Targets = ["domain:video.example"], Dns = ["1.1.1.1"] }, default).ConfigureAwait(false);
        Assert.True(criterion.IsOk, criterion.Message);
        var added = await store.AddAsync(new RouteRule { Name = "video", RuleIds = [criterion.Record!.Id], Action = "direct" }, default).ConfigureAwait(false);
        Assert.True(added.IsOk, added.Message);
        Assert.True(added.Record!.Position < fallback.Position);
        await fixture.IntegrityAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task AnExistingInstallationWithoutSavedResolverKeepsItsImplicitDns()
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "global").ConfigureAwait(false);
        await fixture.SqlAsync("DELETE FROM Resolver").ConfigureAwait(false);
        var before = await fixture.ProtectedAsync().ConfigureAwait(false);

        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);

        var rows = await new RouteStore(fixture.Db).TableAsync(default).ConfigureAwait(false);
        var fallback = rows.Single(row => row.Position == int.MaxValue);
        var criterion = Assert.Single(fallback.Criteria);
        Assert.Equal(TrafficDnsMode.Custom, criterion.DnsMode);
        Assert.Equal(DnsDefaults.Upstreams, criterion.Dns);
        var plan = RoutePlan.Build([fallback], [], GeoIndex.Load([], new MemoryGeoFiles()), []) with { HostDns = ["192.0.2.53"] };
        Assert.Equal(DnsDefaults.Upstreams, DnsRouting.Select(plan, "legacy.example").Servers);
        Assert.Equal(before.Digest, (await fixture.ProtectedAsync(before.Columns).ConfigureAwait(false)).Digest);
        await fixture.IntegrityAsync().ConfigureAwait(false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailureAfterCopyingRoutingDataRollsBackTheWholeMigrationAndCanBeRetried(bool cancellation)
    {
        using var stop = new CancellationTokenSource();
        var fault = new MigrationFault { Cancel = cancellation ? stop : null };
        using var fixture = new PriorDatabase(fault);
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.Db.GetService<IMigrator>().MigrateAsync("20261125184754_ProxyNameDefaults").ConfigureAwait(false);
        var protectedBefore = await fixture.ProtectedAsync().ConfigureAwait(false);
        var before = await fixture.AllAsync().ConfigureAwait(false);
        fault.Armed = true;

        if (cancellation)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Db.Database.MigrateAsync(stop.Token)).ConfigureAwait(false);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.Database.MigrateAsync()).ConfigureAwait(false);
        }

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(before, await fixture.AllAsync().ConfigureAwait(false));
        Assert.Equal(protectedBefore.Digest, (await fixture.ProtectedAsync(protectedBefore.Columns).ConfigureAwait(false)).Digest);
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('TrafficRules','RoutingEntries','RoutingEntryRules')").ConfigureAwait(false));
        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);
        await fixture.IntegrityAsync().ConfigureAwait(false);
        await fixture.ObsoleteAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task GeneratedNamesAvoidCaseInsensitiveCollisionsWithLegacyNames()
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE Rules SET Name = 'default dns' WHERE Id = 41").ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE AddressGroups SET Name = 'DEFAULT DNS' WHERE Id = 12").ConfigureAwait(false);
        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);
        var rows = await new RouteStore(fixture.Db).TableAsync(default).ConfigureAwait(false);
        Assert.Equal("default dns", rows.Single(row => row.Id == 41).Name);
        Assert.Equal(rows.Count, rows.Select(row => row.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var criteria = await new TrafficRuleStore(fixture.Db).ListAsync(default).ConfigureAwait(false);
        Assert.Equal(criteria.Count, criteria.Select(rule => rule.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(criteria, criterion => Assert.InRange(criterion.Name.Length, 1, RouteRules.MaxNameLength));
    }

    [Fact]
    public async Task MigratedBasicEntriesDoNotDuplicateExistingUserEntryNames()
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE Rules SET Name = 'BASIC-BLOCK' WHERE Id = 41").ConfigureAwait(false);
        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);
        var rows = await new RouteStore(fixture.Db).TableAsync(default).ConfigureAwait(false);
        Assert.Equal("BASIC-BLOCK", rows.Single(row => row.Id == 41).Name);
        Assert.NotEqual("basic-block", rows.Single(row => row.Id == -1).Name);
        Assert.Equal(rows.Count, rows.Select(row => row.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task AMissingLegacyGroupDoesNotBecomeAnUnrestrictedRoute()
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE Rules SET Groups = '999999' WHERE Id = 41").ConfigureAwait(false);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Db.Database.MigrateAsync()).ConfigureAwait(false);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM Rules WHERE Id = 41 AND Groups = '999999'").ConfigureAwait(false));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name = 'RoutingEntries'").ConfigureAwait(false));
    }

    [Fact]
    public async Task PreviouslyFaultedLongRawTargetsStayInactiveWhileValidLongGroupsStayActive()
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        var addresses = string.Join(", ", Enumerable.Range(1, 100).Select(number => "192.0.2." + number));
        await fixture.SqlAsync("UPDATE RouteBasics SET Block = '" + addresses + "' WHERE Id = 1").ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE AddressGroups SET Targets = '" + addresses + "' WHERE Id = 11").ConfigureAwait(false);

        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);

        var rows = await new RouteStore(fixture.Db).TableAsync(default).ConfigureAwait(false);
        var legacy = Assert.Single(rows, row => row.Id == -1);
        Assert.False(legacy.IsEnabled);
        Assert.Equal(100, legacy.Targets.Count);
        var valid = Assert.Single(rows, row => row.Id == 41);
        Assert.True(valid.IsEnabled);
        Assert.Equal(100, valid.Criteria.Single(rule => rule.Id == 11).Targets.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedLegacyGroupsKeepEveryReferenceAndChangesReachEveryEntry(bool sameDns)
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE AddressGroups SET Targets = 'domain:shared.example', Dns = '' WHERE Id = 11").ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE Rules SET Targets = '', Groups = '11', Outbound = 'edge', Clients = 'canary' WHERE Id = 41").ConfigureAwait(false);
        await fixture.SqlAsync("INSERT INTO Rules (Id, Name, Position, IsEnabled, Action, Outbound, Targets, Groups, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc) SELECT 44, 'second-shared', 150, IsEnabled, Action, 'second', '', '11', '', Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc FROM Rules WHERE Id = 41").ConfigureAwait(false);
        if (sameDns)
        {
            await fixture.SqlAsync("UPDATE Outbounds SET Dns = '9.9.9.9' WHERE Name = 'second'").ConfigureAwait(false);
        }
        var before = await fixture.ProtectedAsync().ConfigureAwait(false);

        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);

        var store = new RouteStore(fixture.Db);
        var definitions = new TrafficRuleStore(fixture.Db);
        var shared = (await definitions.FindAsync(11, default).ConfigureAwait(false))!;
        Assert.Equal(sameDns ? TrafficDnsMode.Custom : TrafficDnsMode.Default, shared.DnsMode);
        Assert.Equal(sameDns ? new[] { "9.9.9.9" } : [], shared.Dns);
        Assert.Empty(shared.DnsMembers);
        var rows = await store.TableAsync(default).ConfigureAwait(false);
        foreach (var id in new long[] { 41, 44 })
        {
            var row = Assert.Single(rows, one => one.Id == id);
            Assert.Equal([11L], row.RuleIds);
            Assert.Equal(shared.Targets, Assert.Single(row.Criteria).Targets);
        }
        Assert.DoesNotContain(await definitions.ListAsync(default).ConfigureAwait(false), one => one.Name is "video-route" or "second-shared");
        var ways = await new OutboundStore(fixture.Db).ListAsync(default).ConfigureAwait(false);
        var clients = new[] { new TunnelClient { Name = "canary", Address = ["10.7.0.2/32"] } };
        var plan = RoutePlan.Build(rows, ways, GeoIndex.Load([], new MemoryGeoFiles()), ["wg-canary"], clients: clients);
        Assert.Equal(["9.9.9.9"], DnsRouting.Select(plan, "shared.example", IPAddress.Parse("10.7.0.2")).Servers);
        Assert.Equal(sameDns ? new[] { "9.9.9.9" } : ["8.8.8.8"], DnsRouting.Select(plan, "shared.example").Servers);

        var changed = await definitions.ChangeAsync(shared.Id, shared with { Targets = ["domain:changed.example"] }, default).ConfigureAwait(false);
        Assert.True(changed.IsOk, changed.Message);
        rows = await store.TableAsync(default).ConfigureAwait(false);
        foreach (var id in new long[] { 41, 44 })
        {
            var row = Assert.Single(rows, one => one.Id == id);
            Assert.Equal([11L], row.RuleIds);
            Assert.Equal(["domain:changed.example"], row.Targets);
        }
        Assert.Equal(before.Digest, (await fixture.ProtectedAsync(before.Columns).ConfigureAwait(false)).Digest);
        await fixture.IntegrityAsync().ConfigureAwait(false);
    }

    [Theory]
    [InlineData("")]
    [InlineData(", \t\r\n")]
    public async Task AReferencedEmptyLegacyGroupCannotBecomeACatchAll(string targets)
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE AddressGroups SET Targets = '" + targets + "' WHERE Id = 11").ConfigureAwait(false);

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Db.Database.MigrateAsync()).ConfigureAwait(false);

        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM Rules WHERE Id = 41").ConfigureAwait(false));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name = 'RoutingEntries'").ConfigureAwait(false));
    }

    [Fact]
    public async Task ASeparatorOnlyRawTargetDoesNotBecomeACatchAll()
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE Rules SET Targets = ', \t\r\n', Groups = '11' WHERE Id = 41").ConfigureAwait(false);

        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);

        var row = (await new RouteStore(fixture.Db).TableAsync(default).ConfigureAwait(false)).Single(one => one.Id == 41);
        Assert.Equal([11L], row.RuleIds);
        Assert.Equal(["domain:video.example", "domain:shared.example"], row.Targets);
        Assert.All(row.Criteria, criterion => Assert.NotEmpty(criterion.Targets));
        await fixture.IntegrityAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task AnExcessiveLegacyReferenceListStaysInactiveAfterDeduplication()
    {
        using var fixture = new PriorDatabase();
        await fixture.SeedAsync(Beta, "routing").ConfigureAwait(false);
        await fixture.SqlAsync("UPDATE Rules SET Groups = '" + string.Join(",", Enumerable.Repeat("11", 65)) + "' WHERE Id = 41").ConfigureAwait(false);

        await fixture.Db.Database.MigrateAsync().ConfigureAwait(false);

        var row = Assert.Single(await new RouteStore(fixture.Db).TableAsync(default).ConfigureAwait(false), one => one.Id == 41);
        Assert.False(row.IsEnabled);
        Assert.Equal(11L, row.RuleIds[0]);
        Assert.Equal(2, row.RuleIds.Count);
        await fixture.IntegrityAsync().ConfigureAwait(false);
    }

    private sealed class MigrationFault : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public CancellationTokenSource? Cancel { get; init; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("DROP TABLE AddressGroups;", StringComparison.Ordinal))
            {
                Armed = false;
                Cancel?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("injected routing migration failure");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed record ProtectedSnapshot(Dictionary<string, string[]> Columns, string Digest);

    private sealed class PriorDatabase : IDisposable
    {
        private const string Time = "2026-11-24 10:00:00+00:00";
        private static readonly string[] ProtectedTables = ["Configs", "Clients", "Templates", "Subscription", "Panel", "RoutingPresets"];
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"ag-routing-{Guid.NewGuid():N}.db");
        public AppDbContext Db { get; }

        public PriorDatabase(DbCommandInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_path}");
            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }
            Db = new AppDbContext(options.Options);
        }

        public async Task SeedAsync(string version, string mode)
        {
            await Db.GetService<IMigrator>().MigrateAsync(version).ConfigureAwait(false);
            await InsertAsync("Configs", new() { ["Id"] = 9001L, ["Name"] = "wg-canary", ["Host"] = "vpn.example", ["ListenPort"] = 443L, ["Address"] = "10.7.0.1/24, fd77::1/64", ["PrivateKey"] = "synthetic-server-private", ["PublicKey"] = "synthetic-server-public", ["PresharedKey"] = "synthetic-shared-key" }).ConfigureAwait(false);
            await InsertAsync("Templates", new() { ["Id"] = 9003L, ["Name"] = "canary-template", ["AllowedIps"] = "10.7.0.1/32, fd77::1/128", ["Dns"] = "10.7.0.1", ["ProxyIds"] = "[]" }).ConfigureAwait(false);
            await InsertAsync("Clients", new() { ["Id"] = 9002L, ["ConfigId"] = 9001L, ["TemplateId"] = 9003L, ["Name"] = "canary", ["Address"] = "10.7.0.2/32, fd77::2/128", ["PrivateKey"] = "synthetic-client-private", ["PublicKey"] = "synthetic-client-public", ["PresharedKey"] = "synthetic-client-psk", ["SubscriptionId"] = "canary-subscription-keep", ["Dns"] = "10.7.0.1" }).ConfigureAwait(false);
            await InsertAsync("Subscription", new() { ["Id"] = 1L, ["Path"] = "canary-sub-path", ["Title"] = "Canary" }).ConfigureAwait(false);
            await InsertAsync("Panel", new() { ["Id"] = 1L, ["Path"] = "canary-panel-path", ["NameTemplate"] = "{CLIENT}", ["ProxyNameTemplate"] = "custom-{PROXY}" }).ConfigureAwait(false);
            await InsertAsync("RoutingPresets", new() { ["Id"] = 9004L, ["Name"] = "canary-preset", ["Uid"] = "c1e56da5-e294-4b4c-9ca7-1de9a42b6d24", ["Proxy"] = "domain:kept.example" }).ConfigureAwait(false);
            await InsertAsync("Outbounds", new() { ["Id"] = 1L, ["Name"] = "edge", ["Kind"] = "local", ["Mark"] = 42501L, ["Table"] = 254L, ["IsEnabled"] = 1L, ["Dns"] = "9.9.9.9" }).ConfigureAwait(false);
            await InsertAsync("Outbounds", new() { ["Id"] = 2L, ["Name"] = "second", ["Kind"] = "local", ["Mark"] = 42502L, ["Table"] = 254L, ["IsEnabled"] = 1L, ["Dns"] = "8.8.8.8" }).ConfigureAwait(false);
            await InsertAsync("Balancers", new() { ["Id"] = 1L, ["Name"] = "pool", ["Members"] = "edge, second", ["Strategy"] = "priority", ["IsEnabled"] = 1L }).ConfigureAwait(false);
            await InsertAsync("Resolver", new() { ["Id"] = 1L, ["Selection"] = mode, ["Outbound"] = "second", ["Upstreams"] = "tls://1.0.0.1", ["Port"] = 53L, ["IsEnabled"] = 1L, ["FilesMode"] = 5L, ["NameMinutes"] = 60L, ["CacheSize"] = 128L, ["MinTtl"] = 60L, ["MaxTtl"] = 3600L }).ConfigureAwait(false);
            if (version != Stable)
            {
                await InsertAsync("AddressGroups", new() { ["Id"] = 11L, ["Name"] = "video", ["Targets"] = "domain:video.example, domain:shared.example", ["Dns"] = "1.1.1.1" }).ConfigureAwait(false);
                await InsertAsync("AddressGroups", new() { ["Id"] = 12L, ["Name"] = "ads", ["Targets"] = "domain:ads.example, domain:shared.example", ["Dns"] = "8.8.4.4" }).ConfigureAwait(false);
            }
            await InsertAsync("Rules", new() { ["Id"] = 41L, ["Name"] = "video-route", ["Position"] = 100L, ["IsEnabled"] = 1L, ["Action"] = "out", ["Outbound"] = "pool", ["Targets"] = version == Stable ? "domain:base.example, domain:video.example, domain:shared.example, domain:ads.example" : "domain:base.example", ["Groups"] = "11, 12", ["Protocol"] = "any" }).ConfigureAwait(false);
            await InsertAsync("Rules", new() { ["Id"] = 42L, ["Name"] = "limited", ["Position"] = 200L, ["IsEnabled"] = 1L, ["Action"] = "direct", ["Targets"] = "192.0.2.0/24, 2001:db8::/32", ["Clients"] = "canary", ["Inbounds"] = "wg-canary", ["Ports"] = "443", ["SourcePorts"] = "50000-50010", ["Protocol"] = "tcp" }).ConfigureAwait(false);
            await InsertAsync("Rules", new() { ["Id"] = 43L, ["Name"] = "blocked", ["Position"] = 300L, ["IsEnabled"] = 1L, ["Action"] = "block", ["Targets"] = "domain:blocked.example", ["Protocol"] = "any" }).ConfigureAwait(false);
            await InsertAsync("RouteBasics", new() { ["Id"] = 1L, ["Direct"] = "domain:legacy-direct.example", ["Block"] = "domain:legacy-block.example" }).ConfigureAwait(false);
        }

        private async Task InsertAsync(string table, Dictionary<string, object> supplied)
        {
            var columns = await ColumnsAsync(table).ConfigureAwait(false);
            var values = new Dictionary<string, object>();
            foreach (var column in columns)
            {
                if (supplied.TryGetValue(column.Name, out var value))
                {
                    values[column.Name] = value;
                }
                else if (column.Required && !column.HasDefault && !column.Key)
                {
                    values[column.Name] = column.Name.EndsWith("Utc", StringComparison.Ordinal) ? Time : column.Type == "INTEGER" ? 0L : string.Empty;
                }
            }
            var command = Db.Database.GetDbConnection().CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = $"INSERT INTO {table} ({string.Join(',', values.Keys.Select(name => '"' + name + '"'))}) VALUES ({string.Join(',', values.Keys.Select((_, at) => "$p" + at))})";
                var at = 0;
                foreach (var value in values.Values)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "$p" + at++;
                    parameter.Value = value;
                    command.Parameters.Add(parameter);
                }
                await OpenAsync().ConfigureAwait(false);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        public async Task<ProtectedSnapshot> ProtectedAsync(Dictionary<string, string[]>? columns = null)
        {
            var found = columns ?? new Dictionary<string, string[]>();
            if (columns is null)
            {
                foreach (var table in ProtectedTables)
                {
                    found[table] = (await ColumnsAsync(table).ConfigureAwait(false)).Select(column => column.Name).ToArray();
                }
            }
            return new ProtectedSnapshot(found, await DigestAsync(found).ConfigureAwait(false));
        }

        public async Task<string> AllAsync()
        {
            var tables = await ReadAsync("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsLock' ORDER BY name").ConfigureAwait(false);
            var columns = new Dictionary<string, string[]>();
            foreach (var table in tables.Select(row => (string)row[0]))
            {
                columns[table] = (await ColumnsAsync(table).ConfigureAwait(false)).Select(column => column.Name).ToArray();
            }
            return await DigestAsync(columns).ConfigureAwait(false);
        }

        private async Task<string> DigestAsync(Dictionary<string, string[]> columns)
        {
            var text = new StringBuilder();
            foreach (var pair in columns.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                text.Append(pair.Key).Append(JsonSerializer.Serialize(pair.Value));
                var rows = await ReadAsync($"SELECT {string.Join(',', pair.Value.Select(name => '"' + name + '"'))} FROM \"{pair.Key}\" ORDER BY {string.Join(',', pair.Value.Select(name => '"' + name + '"'))}").ConfigureAwait(false);
                text.Append(JsonSerializer.Serialize(rows));
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        private async Task<IReadOnlyList<(string Name, string Type, bool Required, bool HasDefault, bool Key)>> ColumnsAsync(string table)
        {
            var rows = await ReadAsync($"PRAGMA table_info(\"{table}\")").ConfigureAwait(false);
            return rows.Select(row => ((string)row[1], (string)row[2], (long)row[3] != 0, row[4] is not DBNull, (long)row[5] != 0)).ToArray();
        }

        public async Task<long> ScalarAsync(string sql) => Convert.ToInt64((await ReadAsync(sql).ConfigureAwait(false))[0][0]);

        public async Task SqlAsync(string sql) => await Db.Database.ExecuteSqlRawAsync(sql).ConfigureAwait(false);

        private async Task<IReadOnlyList<object[]>> ReadAsync(string sql)
        {
            await OpenAsync().ConfigureAwait(false);
            var command = Db.Database.GetDbConnection().CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = sql;
                var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    var rows = new List<object[]>();
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        var row = new object[reader.FieldCount];
                        reader.GetValues(row);
                        rows.Add(row);
                    }
                    return rows;
                }
            }
        }

        private Task OpenAsync() => Db.Database.GetDbConnection().State == System.Data.ConnectionState.Open ? Task.CompletedTask : Db.Database.OpenConnectionAsync();

        public async Task IntegrityAsync()
        {
            Assert.Equal("ok", (await ReadAsync("PRAGMA integrity_check").ConfigureAwait(false))[0][0]);
            Assert.Empty(await ReadAsync("PRAGMA foreign_key_check").ConfigureAwait(false));
        }

        public async Task ObsoleteAsync()
        {
            Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('AddressGroups','RouteBasics','Rules')").ConfigureAwait(false));
            foreach (var table in new[] { "Outbounds", "Balancers" })
            {
                Assert.Contains(await ColumnsAsync(table).ConfigureAwait(false), column => column.Name == "Dns");
            }
            Assert.DoesNotContain(await ColumnsAsync("Resolver").ConfigureAwait(false), column => column.Name is "Selection" or "Outbound" or "Upstreams");
        }

        public void Dispose()
        {
            Db.Dispose();
            Pools.Clear(_path);
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var file = _path + suffix;
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
