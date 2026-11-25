using System.Net;
using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public sealed class DnsRoutingStoreTests
{
    [Fact]
    public async Task ReusableRulesKeepDnsAndMemberOverridesAcrossStorage()
    {
        using var bench = new Bench();
        var store = new TrafficRuleStore(bench.Db);
        var made = await store.AddAsync(new TrafficRule
        {
            Name = "video", Targets = ["domain:example.test"], Dns = ["tls://9.9.9.9"],
            DnsMembers = new Dictionary<string, IReadOnlyList<string>> { ["direct"] = ["https://1.1.1.1/dns-query"] },
        }, default).ConfigureAwait(false);
        Assert.True(made.IsOk, made.Message);
        var first = await bench.Rules.AddAsync(new RouteRule { Name = "first", RuleIds = [made.Record!.Id], Action = "direct" }, default).ConfigureAwait(false);
        var second = await bench.Rules.AddAsync(new RouteRule { Name = "second", RuleIds = [made.Record.Id], Action = "block" }, default).ConfigureAwait(false);
        Assert.True(first.IsOk, first.Message);
        Assert.True(second.IsOk, second.Message);
        var changed = await store.ChangeAsync(made.Record.Id, made.Record with { Dns = ["8.8.8.8"] }, default).ConfigureAwait(false);
        Assert.True(changed.IsOk, changed.Message);
        bench.Db.ChangeTracker.Clear();
        var rows = await bench.Rules.TableAsync(default).ConfigureAwait(false);
        Assert.All(rows, row => Assert.Equal(["8.8.8.8"], Assert.Single(row.Criteria).Dns));
        Assert.All(rows, row => Assert.Equal(["https://1.1.1.1/dns-query"], Assert.Single(row.Criteria).DnsMembers["direct"]));
        Assert.Equal("traffic-rule-in-use", (await store.RemoveAsync(made.Record.Id, default).ConfigureAwait(false)).Fault?.Code);
    }

    [Fact]
    public async Task InheritedDnsModeAndProcessingResolversSurviveStorage()
    {
        using var bench = new Bench();
        var outbound = Assert.Single(await bench.Outbounds.ListAsync(default).ConfigureAwait(false));
        var changed = await bench.Outbounds.ChangeAsync(outbound.Id, outbound with { Dns = ["9.9.9.9"] }, default).ConfigureAwait(false);
        Assert.True(changed.IsOk, changed.Message);
        var balancer = await bench.Balancers.AddAsync(new AmneziaGeo.Server.Routing.Balance.Balancer { Name = "dns-pool", Members = [outbound.Name], Dns = ["tls://1.1.1.1"] }, default).ConfigureAwait(false);
        Assert.True(balancer.IsOk, balancer.Message);
        var store = new TrafficRuleStore(bench.Db);
        var made = await store.AddAsync(new TrafficRule { Name = "inherited", Targets = ["domain:example.test"], DnsMode = TrafficDnsMode.Default }, default).ConfigureAwait(false);
        Assert.True(made.IsOk, made.Message);
        bench.Db.ChangeTracker.Clear();
        var held = await store.FindAsync(made.Record!.Id, default).ConfigureAwait(false);
        Assert.Equal(TrafficDnsMode.Default, held!.DnsMode);
        Assert.Empty(held.Dns);
        Assert.Equal(["9.9.9.9"], (await bench.Outbounds.FindAsync(outbound.Id, default).ConfigureAwait(false))!.Dns);
        Assert.Equal(["tls://1.1.1.1"], (await bench.Balancers.FindAsync(balancer.Record!.Id, default).ConfigureAwait(false))!.Dns);
    }

    [Fact]
    public async Task ReorderingCriteriaPreservesReferencesAndDnsPriority()
    {
        using var bench = new Bench();
        var store = new TrafficRuleStore(bench.Db);
        var first = (await store.AddAsync(new TrafficRule { Name = "one", Targets = ["domain:example.test"], Dns = ["1.1.1.1"] }, default).ConfigureAwait(false)).Record!;
        var second = (await store.AddAsync(new TrafficRule { Name = "two", Targets = ["domain:example.test"], Dns = ["8.8.8.8"] }, default).ConfigureAwait(false)).Record!;
        var made = await bench.Rules.AddAsync(new RouteRule { Name = "order", RuleIds = [first.Id, second.Id], Action = "direct" }, default).ConfigureAwait(false);
        Assert.True(made.IsOk, made.Message);
        var changed = await bench.Rules.ChangeAsync(made.Record!.Id, made.Record with { RuleIds = [second.Id, first.Id] }, default).ConfigureAwait(false);
        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal([second.Id, first.Id], changed.Record!.RuleIds);
        Assert.Equal([second.Id, first.Id], (await bench.Rules.FindAsync(made.Record.Id, default).ConfigureAwait(false))!.RuleIds);
        Assert.Equal(2, await bench.Db.TrafficRules.CountAsync().ConfigureAwait(false));
    }

    [Fact]
    public async Task RenamingAMemberCarriesDnsOverridesAlong()
    {
        using var bench = new Bench();
        var way = Assert.Single(await bench.Outbounds.ListAsync(default).ConfigureAwait(false));
        var store = new TrafficRuleStore(bench.Db);
        var made = await store.AddAsync(new TrafficRule { Name = "named", DnsMembers = new Dictionary<string, IReadOnlyList<string>> { [way.Name] = ["1.1.1.1"] } }, default).ConfigureAwait(false);
        Assert.True(made.IsOk, made.Message);
        var changed = await bench.Outbounds.ChangeAsync(way.Id, way with { Name = "renamed" }, default).ConfigureAwait(false);
        Assert.True(changed.IsOk, changed.Message);
        var held = await store.FindAsync(made.Record!.Id, default).ConfigureAwait(false);
        Assert.Equal(["1.1.1.1"], held!.DnsMembers["renamed"]);
        Assert.DoesNotContain(way.Name, held.DnsMembers.Keys);
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
        await store.SaveAsync(standIns.Held(), default).ConfigureAwait(false);
        var restored = new DnsStandIns();
        restored.Restore(await store.ListAsync(default).ConfigureAwait(false));
        Assert.Equal("192.0.2.1", restored.Real(first)!.ToString());
        Assert.Equal("192.0.2.2", restored.Real(second)!.ToString());
        Assert.Equal(first, restored.Take("EXAMPLE.TEST.", [IPAddress.Parse("192.0.2.1")], out _, "first"));
        Assert.Equal(2, restored.Count);
    }

    [Theory]
    [InlineData("nowhere")]
    [InlineData("https://resolver.example/dns-query")]
    public void RuleDnsRequiresExplicitResolverAddresses(string server)
    {
        Assert.Equal("bad-dns", RouteRules.Check(new TrafficRule { Name = "video", Targets = ["example.test"], Dns = [server] })?.Code);
    }
}
