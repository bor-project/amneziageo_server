using AmneziaGeo.Server.Api.Balancers;
using System.Net;
using System.Text.Json;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Dal.Exchange;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public sealed class DnsRoutingStoreTests
{
    [Fact]
    public void OlderJsonRequestsKeepNewDnsSettings()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var oldDns = JsonSerializer.Deserialize<DnsRequest>("""
            {"isEnabled":true,"port":53,"upstreams":["1.1.1.1"],"outbound":"","listen":[],"nameMinutes":60,"cacheSize":128,"minTtl":60,"maxTtl":3600}
            """, options)!;
        Assert.Equal(DnsSelection.Routing, DnsAnswers.Draft(oldDns, DnsDefaults.Fresh with { Selection = DnsSelection.Routing }).Selection);
        var oldBalancer = JsonSerializer.Deserialize<BalanceRequest>("""
            {"name":"mix","isEnabled":true,"strategy":"priority","members":["direct"]}
            """, options)!;
        Assert.Equal(["9.9.9.9"], BalanceAnswers.Draft(oldBalancer,
            new Balancer { Name = "mix", Members = ["direct"], Dns = ["9.9.9.9"] }).Dns);
    }

    [Fact]
    public async Task AnUpgradeKeepsExistingDnsValuesAndDoesNotActivateRoutingPolicies()
    {
        using var bench = new Bench();
        var outbound = await bench.Db.Outbounds.FirstAsync().ConfigureAwait(false);
        outbound.Dns = "9.9.9.9";
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var group = await bench.Rules.ImportGroupAsync(new AddressGroup { Name = "kept", Targets = ["example.test"] }, CancellationToken.None).ConfigureAwait(false);
        var balancer = await bench.Balancers.AddAsync(new Balancer { Name = "mix", Members = [outbound.Name] }, CancellationToken.None).ConfigureAwait(false);
        await bench.Resolver.SaveAsync(DnsDefaults.Fresh with { Upstreams = ["1.1.1.1"], Outbound = balancer.Record!.Name }, CancellationToken.None).ConfigureAwait(false);
        await new StandInStore(bench.Db).SaveAsync([new DnsStandIn("example.test", IPAddress.Parse("198.18.0.7"), IPAddress.Parse("192.0.2.7"), bench.Clock.GetUtcNow())], CancellationToken.None).ConfigureAwait(false);
        await bench.Db.GetService<IMigrator>().MigrateAsync("RoutingTable").ConfigureAwait(false);
        bench.Db.ChangeTracker.Clear();

        await bench.Db.Database.MigrateAsync().ConfigureAwait(false);
        await bench.Db.Database.MigrateAsync().ConfigureAwait(false);
        var settings = await bench.Resolver.ReadAsync(CancellationToken.None).ConfigureAwait(false);
        var groups = await bench.Rules.GroupsAsync(CancellationToken.None).ConfigureAwait(false);
        var balancers = await bench.Balancers.ListAsync(CancellationToken.None).ConfigureAwait(false);
        var ways = await bench.Outbounds.ListAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(DnsSelection.Global, settings.Selection);
        Assert.Equal(["1.1.1.1"], settings.Upstreams);
        Assert.Equal("mix", settings.Outbound);
        Assert.Equal(group.Record!.Id, Assert.Single(groups).Id);
        Assert.Empty(Assert.Single(groups).Dns);
        Assert.Empty(Assert.Single(balancers).Dns);
        Assert.Equal(["9.9.9.9"], ways.Single(one => one.Id == outbound.Id).Dns);
        var stood = Assert.Single(await new StandInStore(bench.Db).ListAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal("example.test", stood.Name);
        Assert.Equal("198.18.0.7", stood.Address.ToString());
        Assert.Empty(stood.Scope);
        Assert.False(bench.Db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task StandInAddressesAndRealAddressesRemainSeparateAfterRestart()
    {
        using var bench = new Bench();
        var standIns = new DnsStandIns();
        var first = standIns.Take("example.test", [IPAddress.Parse("192.0.2.1")], out _, "first");
        var second = standIns.Take("example.test", [IPAddress.Parse("192.0.2.2")], out _, "second");
        Assert.NotEqual(first, second);
        var store = new StandInStore(bench.Db);
        await store.SaveAsync(standIns.Held(), CancellationToken.None).ConfigureAwait(false);
        var restored = new DnsStandIns();
        restored.Restore(await store.ListAsync(CancellationToken.None).ConfigureAwait(false));

        Assert.Equal("192.0.2.1", restored.Real(first)!.ToString());
        Assert.Equal("192.0.2.2", restored.Real(second)!.ToString());
        Assert.Equal(first, restored.Take("EXAMPLE.TEST.", [IPAddress.Parse("192.0.2.1")], out _, "first"));
        Assert.Equal(2, restored.Count);
    }

    [Fact]
    public async Task PoliciesAndOverridesSurviveStorageAndOlderApiDrafts()
    {
        using var bench = new Bench();
        var settings = DnsDefaults.Fresh with { Selection = DnsSelection.Routing };
        Assert.True((await bench.Resolver.SaveAsync(settings, CancellationToken.None).ConfigureAwait(false)).IsOk);
        Assert.Equal(settings.Selection, (await bench.Resolver.ReadAsync(CancellationToken.None).ConfigureAwait(false)).Selection);
        var request = new DnsRequest(true, 53, settings.Upstreams, "", [], 60, 128, 60, 3600);
        Assert.Equal(settings.Selection, DnsAnswers.Draft(request, settings).Selection);

        var made = await bench.Balancers.AddAsync(new Balancer { Name = "mix", Members = ["direct"], Dns = ["https://9.9.9.9/dns-query"] }, CancellationToken.None).ConfigureAwait(false);
        Assert.True(made.IsOk, made.Message);
        var older = BalanceAnswers.Draft(new BalanceRequest("mix", true, BalanceStrategy.Priority, ["direct"]), made.Record);
        Assert.Equal(made.Record!.Dns, older.Dns);
        var changed = await bench.Balancers.ChangeAsync(made.Record.Id, older, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(made.Record.Dns, changed.Record!.Dns);
    }

    [Fact]
    public async Task AnExportCarriesClearingAnExistingDnsOverride()
    {
        using var source = new Bench();
        using var target = new Bench();
        await source.Rules.ImportGroupAsync(new AddressGroup { Name = "video", Targets = ["example.test"] }, CancellationToken.None).ConfigureAwait(false);
        await target.Rules.ImportGroupAsync(new AddressGroup { Name = "video", Targets = ["example.test"], Dns = ["9.9.9.9"] }, CancellationToken.None).ConfigureAwait(false);
        var exported = await RoutingExport.TakeAsync(source.Rules, source.Presets, source.Geo, CancellationToken.None).ConfigureAwait(false);
        var read = RoutingFile.Read(RoutingExport.Text(exported));
        Assert.True(read.IsOk, read.Message);
        Assert.NotNull(Assert.Single(read.Doc!.AddressGroups!).Dns);
        Assert.Empty(Assert.Single(read.Doc.AddressGroups!).Dns!);
        var plan = await RoutingImport.PlanAsync(read.Doc, target.Rules, target.Presets, target.Geo, target.Outbounds, target.Clients, CancellationToken.None).ConfigureAwait(false);

        var result = await RoutingImport.ApplyAsync(plan, target.Rules, target.Presets, target.Geo, CancellationToken.None).ConfigureAwait(false);

        Assert.Empty(result.Refused);
        Assert.Empty(Assert.Single(await target.Rules.GroupsAsync(CancellationToken.None).ConfigureAwait(false)).Dns);
    }

    [Fact]
    public async Task RoutingExportsCarryOverridesAndLegacyImportsKeepExistingOverrides()
    {
        using var source = new Bench();
        using var target = new Bench();
        var made = await source.Rules.ImportGroupAsync(new AddressGroup
        {
            Name = "video",
            Targets = ["example.test"],
            Dns = ["tls://9.9.9.9"],
        }, CancellationToken.None).ConfigureAwait(false);
        Assert.Null(made.Fault);
        var exported = await RoutingExport.TakeAsync(source.Rules, source.Presets, source.Geo, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(3, exported.Version);
        var read = RoutingFile.Read(RoutingExport.Text(exported));
        Assert.True(read.IsOk, read.Message);
        var plan = await RoutingImport.PlanAsync(read.Doc!, target.Rules, target.Presets, target.Geo, target.Outbounds, target.Clients, CancellationToken.None).ConfigureAwait(false);
        var result = await RoutingImport.ApplyAsync(plan, target.Rules, target.Presets, target.Geo, CancellationToken.None).ConfigureAwait(false);
        Assert.Empty(result.Refused);
        Assert.Equal(made.Record!.Dns, Assert.Single(await target.Rules.GroupsAsync(CancellationToken.None).ConfigureAwait(false)).Dns);

        var legacy = exported with { Version = 2, AddressGroups = [new AddressGroupBlock("video", ["other.test"])] };
        var legacyPlan = await RoutingImport.PlanAsync(legacy, target.Rules, target.Presets, target.Geo, target.Outbounds, target.Clients, CancellationToken.None).ConfigureAwait(false);
        await RoutingImport.ApplyAsync(legacyPlan, target.Rules, target.Presets, target.Geo, CancellationToken.None).ConfigureAwait(false);
        var held = Assert.Single(await target.Rules.GroupsAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(["other.test"], held.Targets);
        Assert.Equal(made.Record.Dns, held.Dns);
    }

    [Theory]
    [InlineData("nowhere")]
    [InlineData("https://resolver.example/dns-query")]
    public void OverridesRequireDnsEndpointsWithoutBootstrapResolution(string server)
    {
        Assert.Equal("bad-dns", BalanceRules.Check(new Balancer { Name = "mix", Members = ["direct"], Dns = [server] })?.Code);
        Assert.Equal("bad-dns", RouteRules.CheckGroup(new AddressGroup { Name = "video", Targets = ["example.test"], Dns = [server] })?.Code);
    }
}
