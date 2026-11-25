using System.Net;
using Microsoft.EntityFrameworkCore;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Dal.Exchange;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public sealed class TrafficRuleTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(-1L)]
    public void AnEntryDraftChecksItsDnsRequirements(long id)
    {
        var world = new DnsWorld
        {
            Settings = DnsDefaults.Settings with { IsEnabled = false },
            TrafficRules =
            [
                new TrafficRule { Id = 1, Name = "names", Targets = ["example.test"] },
                new TrafficRule { Id = 2, Name = "addresses", Targets = ["192.0.2.0/24"], Dns = DnsDefaults.Upstreams },
            ],
        };
        var rule = new RouteRequest("group rule", true, RouteAction.Block, "", [1], [], RouteProtocol.Any);

        var named = DnsChecks.Check(world, new DnsCheckRequest(id, Rule: rule));
        var addresses = DnsChecks.Check(world, new DnsCheckRequest(id, Rule: rule with { RuleIds = [2] }));

        Assert.Contains(named.Findings, finding => finding.Code == "rule-off");
        Assert.Empty(addresses.Findings);
    }

    [Fact]
    public async Task ATrafficRuleIsPersistentAndDoesNotTurnIntoACatchAll()
    {
        using var bench = new Bench();
        var groups = new TrafficRuleStore(bench.Db);
        var made = await groups.AddAsync(new TrafficRule { Name = "video", Targets = ["192.0.2.0/24", "2001:db8::/32"], Dns = DnsDefaults.Upstreams }, CancellationToken.None).ConfigureAwait(false);
        Assert.Null(made.Fault);
        var added = await bench.Rules.AddAsync(RouteDefaults.Fresh("video rule") with
        {
            Action = RouteAction.Block,
            RuleIds = [made.Record!.Id],
        }, CancellationToken.None).ConfigureAwait(false);
        Assert.True(added.IsOk, added.Message);
        var plan = RoutePlan.Build(await bench.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false),
            [], GeoIndex.Load([], new MemoryGeoFiles()), []);

        Assert.Equal(RouteProbe.Block, RouteProbe.Test(plan, Query("192.0.2.7"), (_, _) => false).Verdict);
        Assert.Equal(RouteProbe.Block, RouteProbe.Test(plan, Query("2001:db8::7"), (_, _) => false).Verdict);
        Assert.Equal(RouteProbe.Host, RouteProbe.Test(plan, Query("198.51.100.7"), (_, _) => false).Verdict);
        Assert.Equal(["192.0.2.0/24"], Assert.Single(plan.Legs).Cidrs4);
    }

    [Fact]
    public async Task AReferencedTrafficRuleCannotBeRemovedEvenWhenTheRuleIsDisabled()
    {
        using var bench = new Bench();
        var groups = new TrafficRuleStore(bench.Db);
        var made = await groups.AddAsync(new TrafficRule { Name = "kept", Targets = ["192.0.2.1"], Dns = DnsDefaults.Upstreams }, CancellationToken.None).ConfigureAwait(false);
        await bench.Rules.AddAsync(RouteDefaults.Fresh("disabled") with
        {
            Action = RouteAction.Block,
            IsEnabled = false,
            RuleIds = [made.Record!.Id],
        }, CancellationToken.None).ConfigureAwait(false);

        var removed = await groups.RemoveAsync(made.Record.Id, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal("traffic-rule-in-use", removed.Fault?.Code);
        Assert.Single(await groups.ListAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task InvalidRulesAndMissingReferencesCannotChangeRouting()
    {
        using var bench = new Bench();
        var groups = new TrafficRuleStore(bench.Db);
        var empty = await groups.AddAsync(new TrafficRule { Name = "bad DNS", Dns = ["not a DNS address"] }, CancellationToken.None).ConfigureAwait(false);
        var unknown = await bench.Rules.AddAsync(RouteDefaults.Fresh("bad") with { Action = RouteAction.Block, RuleIds = [123] }, CancellationToken.None).ConfigureAwait(false);

        Assert.NotNull(empty.Fault);
        Assert.Equal("unknown-traffic-rule", unknown.Code);
        Assert.Empty(await bench.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task AnOlderRequestKeepsTheTrafficRuleReferences()
    {
        using var bench = new Bench();
        var groups = new TrafficRuleStore(bench.Db);
        var made = await groups.AddAsync(new TrafficRule { Name = "kept", Targets = ["192.0.2.1"], Dns = DnsDefaults.Upstreams }, CancellationToken.None).ConfigureAwait(false);
        var added = await bench.Rules.AddAsync(RouteDefaults.Fresh("kept") with { Action = RouteAction.Block, RuleIds = [made.Record!.Id] }, CancellationToken.None).ConfigureAwait(false);
        var draft = RouteAnswers.Draft(new RouteRequest("kept", true, RouteAction.Block, "", null, [], "any"), added.Record);

        Assert.Equal([made.Record.Id], draft.RuleIds);
        var changed = await bench.Rules.ChangeAsync(added.Record!.Id, draft, CancellationToken.None).ConfigureAwait(false);
        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal([made.Record.Id], changed.Record!.RuleIds);
    }

    [Fact]
    public async Task AnExportMapsRuleNamesToTheDestinationServersIdentifiers()
    {
        using var source = new Bench();
        using var target = new Bench();
        var original = await source.Rules.ImportCriterionAsync(new TrafficRule { Name = "video", Targets = ["192.0.2.0/24"], Dns = DnsDefaults.Upstreams }, CancellationToken.None).ConfigureAwait(false);
        await target.Rules.ImportCriterionAsync(new TrafficRule { Name = "unrelated", Targets = ["198.51.100.1"], Dns = DnsDefaults.Upstreams }, CancellationToken.None).ConfigureAwait(false);
        await source.Rules.AddAsync(RouteDefaults.Fresh("video") with { Action = RouteAction.Block, RuleIds = [original.Record!.Id] }, CancellationToken.None).ConfigureAwait(false);
        var doc = await RoutingExport.TakeAsync(source.Rules, source.Presets, source.Geo, CancellationToken.None).ConfigureAwait(false);
        var read = RoutingFile.Read(RoutingExport.Text(doc));
        Assert.True(read.IsOk, read.Message);
        Assert.Equal(["video"], Assert.Single(read.Doc!.Entries).Rules);
        var plan = await RoutingImport.PlanAsync(read.Doc, target.Rules, target.Presets, target.Geo, target.Outbounds, target.Clients, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(["video"], plan.NewRules);

        var answer = await RoutingImport.ApplyAsync(plan, target.Rules, target.Presets, target.Geo, CancellationToken.None).ConfigureAwait(false);

        Assert.Empty(answer.Refused);
        var transferred = Assert.Single(await target.Rules.ListCriteriaAsync(CancellationToken.None).ConfigureAwait(false), group => group.Name == "video");
        Assert.NotEqual(original.Record.Id, transferred.Id);
        Assert.Equal([transferred.Id], Assert.Single(await target.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false)).RuleIds);
    }

    [Fact]
    public async Task AnExportKeepsTheReorderedAndDisabledLegacyExceptions()
    {
        using var source = new Bench();
        using var target = new Bench();
        await source.Rules.SaveBasicAsync(new RouteBasic { Block = ["10.0.0.0/8"], Direct = ["192.0.2.1"] }, CancellationToken.None).ConfigureAwait(false);
        var made = await source.Rules.AddAsync(RouteDefaults.Fresh("first") with { Action = RouteAction.Direct }, CancellationToken.None).ConfigureAwait(false);
        await source.Rules.PlaceAsync(made.Record!.Id, 1, CancellationToken.None, table: true).ConfigureAwait(false);
        await source.Rules.SwitchAsync(RouteBasic.BlockId, false, CancellationToken.None).ConfigureAwait(false);
        var doc = await RoutingExport.TakeAsync(source.Rules, source.Presets, source.Geo, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(4, doc.Version);
        var plan = await RoutingImport.PlanAsync(doc, target.Rules, target.Presets, target.Geo, target.Outbounds, target.Clients, CancellationToken.None).ConfigureAwait(false);

        var answer = await RoutingImport.ApplyAsync(plan, target.Rules, target.Presets, target.Geo, CancellationToken.None).ConfigureAwait(false);

        Assert.Empty(answer.Refused);
        var table = await target.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(["first", RouteBasic.BlockName, RouteBasic.DirectName], table.Select(rule => rule.Name));
        Assert.False(table[1].IsEnabled);
        Assert.Equal(RouteBasic.BlockId, table[1].Id);
        Assert.Equal(RouteBasic.DirectId, table[2].Id);
    }

    [Fact]
    public void AVersionOneFileStillReads()
    {
        var read = RoutingFile.Read("{\"format\":\"amneziageo-routing\",\"version\":1,\"rules\":[],\"basic\":{\"direct\":[\"192.0.2.1\"],\"block\":[]}}");

        Assert.True(read.IsOk, read.Message);
        Assert.Equal(["192.0.2.1"], Assert.Single(read.Doc!.Rules).Targets);
        Assert.Equal(RouteAction.Direct, Assert.Single(read.Doc.Entries).Action);
    }

    [Fact]
    public async Task AReusableRuleUpdatesAllEntriesWithoutChangingTheirFiltersOrPriority()
    {
        using var bench = new Bench();
        var definitions = new TrafficRuleStore(bench.Db);
        var made = await definitions.AddAsync(new TrafficRule { Name = "shared", Targets = ["192.0.2.1"], Dns = ["1.1.1.1"] }, CancellationToken.None).ConfigureAwait(false);
        var first = await bench.Rules.AddAsync(RouteDefaults.Fresh("blocked") with { Action = RouteAction.Block, RuleIds = [made.Record!.Id], Clients = ["alice"], Inbounds = ["awg0"], Ports = ["443"] }, CancellationToken.None).ConfigureAwait(false);
        var second = await bench.Rules.AddAsync(RouteDefaults.Fresh("direct") with { Action = RouteAction.Direct, RuleIds = [made.Record.Id], Clients = ["bob"], Protocol = RouteProtocol.Udp }, CancellationToken.None).ConfigureAwait(false);
        Assert.True(first.IsOk, first.Message);
        Assert.True(second.IsOk, second.Message);

        var changed = await definitions.ChangeAsync(made.Record.Id, made.Record with { Targets = ["198.51.100.1"], Dns = ["9.9.9.9"] }, CancellationToken.None).ConfigureAwait(false);
        Assert.Null(changed.Fault);
        var rows = await bench.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal([first.Record!.Id, second.Record!.Id], rows.Select(one => one.Id));
        Assert.All(rows, one => Assert.Equal(["198.51.100.1"], Assert.Single(one.Criteria).Targets));
        Assert.All(rows, one => Assert.Equal(["9.9.9.9"], Assert.Single(one.Criteria).Dns));
        Assert.Equal(["alice"], rows[0].Clients);
        Assert.Equal(["awg0"], rows[0].Inbounds);
        Assert.Equal(["443"], rows[0].Ports);
        Assert.Equal(["bob"], rows[1].Clients);
        Assert.Equal(RouteProtocol.Udp, rows[1].Protocol);
    }

    [Theory]
    [InlineData(RouteAction.Out)]
    [InlineData(RouteAction.Block)]
    [InlineData(RouteAction.Direct)]
    public async Task ReusingDefaultAddressesDoesNotReorderExistingRules(string action)
    {
        using var bench = new Bench();
        var made = await bench.Rules.ImportCriterionAsync(new TrafficRule { Name = "default", DnsMode = TrafficDnsMode.Default }, CancellationToken.None).ConfigureAwait(false);
        var addresses = await bench.Db.TrafficRules.SingleAsync(one => one.Id == made.Record!.Id).ConfigureAwait(false);
        addresses.DefaultKey = "resolver-default";
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var fallback = await bench.Rules.AddAsync(RouteDefaults.Fresh("fallback") with { Action = RouteAction.Direct, RuleIds = [addresses.Id] }, CancellationToken.None).ConfigureAwait(false);
        Assert.True(fallback.IsOk, fallback.Message);
        var entity = await bench.Db.Rules.SingleAsync(one => one.Id == fallback.Record!.Id).ConfigureAwait(false);
        entity.Position = int.MaxValue;
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var first = await bench.Rules.AddAsync(RouteDefaults.Fresh("first") with { Action = action, Outbound = action == RouteAction.Out ? "remote" : "", RuleIds = [addresses.Id], Clients = ["alice"] }, CancellationToken.None).ConfigureAwait(false);
        Assert.True(first.IsOk, first.Message);
        var second = await bench.Rules.AddAsync(RouteDefaults.Fresh("second") with { Action = RouteAction.Block, RuleIds = [addresses.Id], Clients = ["bob"] }, CancellationToken.None).ConfigureAwait(false);
        Assert.True(second.IsOk, second.Message);
        var rows = await bench.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal([first.Record!.Id, second.Record!.Id, fallback.Record!.Id], rows.Select(one => one.Id));
        Assert.Equal(first.Record.Position, rows[0].Position);
        Assert.True(rows[0].Position < rows[1].Position);
        Assert.Equal(int.MaxValue, rows[2].Position);
    }

    private static RouteQuery Query(string address) => new() { Addresses = [IPAddress.Parse(address)], Port = 443 };
}
