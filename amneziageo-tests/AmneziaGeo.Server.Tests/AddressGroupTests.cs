using System.Net;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Dal.Exchange;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public sealed class AddressGroupTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(-1L)]
    public void AGroupDraftChecksItsDnsRequirements(long id)
    {
        var world = new DnsWorld
        {
            Settings = DnsDefaults.Settings with { IsEnabled = false },
            Groups =
            [
                new AddressGroup { Id = 1, Name = "names", Targets = ["example.test"] },
                new AddressGroup { Id = 2, Name = "addresses", Targets = ["192.0.2.0/24"] },
            ],
        };
        var rule = new RouteRequest("group rule", true, RouteAction.Block, "", [], [], RouteProtocol.Any, Groups: [1]);

        var named = DnsChecks.Check(world, new DnsCheckRequest(id, Rule: rule));
        var addresses = DnsChecks.Check(world, new DnsCheckRequest(id, Rule: rule with { Groups = [2] }));

        Assert.Contains(named.Findings, finding => finding.Code == "rule-off");
        Assert.Empty(addresses.Findings);
    }

    [Fact]
    public async Task AGroupIsPersistentAndDoesNotTurnIntoACatchAll()
    {
        using var bench = new Bench();
        var groups = new AddressGroupStore(bench.Db);
        var made = await groups.AddAsync(new AddressGroup { Name = "video", Targets = ["192.0.2.0/24", "2001:db8::/32"] }, CancellationToken.None).ConfigureAwait(false);
        Assert.Null(made.Fault);
        var added = await bench.Rules.AddAsync(RouteDefaults.Fresh("video rule") with
        {
            Action = RouteAction.Block,
            Groups = [made.Record!.Id],
        }, CancellationToken.None).ConfigureAwait(false);
        Assert.True(added.IsOk, added.Message);
        var plan = RoutePlan.Build(await bench.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false),
            [], GeoIndex.Load([], new MemoryGeoFiles()), [], groups: await groups.ListAsync(CancellationToken.None).ConfigureAwait(false));

        Assert.Equal(RouteProbe.Block, RouteProbe.Test(plan, Query("192.0.2.7"), (_, _) => false).Verdict);
        Assert.Equal(RouteProbe.Block, RouteProbe.Test(plan, Query("2001:db8::7"), (_, _) => false).Verdict);
        Assert.Equal(RouteProbe.Host, RouteProbe.Test(plan, Query("198.51.100.7"), (_, _) => false).Verdict);
        Assert.Equal(["192.0.2.0/24"], Assert.Single(plan.Legs).Cidrs4);
    }

    [Fact]
    public async Task AReferencedGroupCannotBeRemovedEvenWhenTheRuleIsDisabled()
    {
        using var bench = new Bench();
        var groups = new AddressGroupStore(bench.Db);
        var made = await groups.AddAsync(new AddressGroup { Name = "kept", Targets = ["192.0.2.1"] }, CancellationToken.None).ConfigureAwait(false);
        await bench.Rules.AddAsync(RouteDefaults.Fresh("disabled") with
        {
            Action = RouteAction.Block,
            IsEnabled = false,
            Groups = [made.Record!.Id],
        }, CancellationToken.None).ConfigureAwait(false);

        var removed = await groups.RemoveAsync(made.Record.Id, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal("address-group-in-use", removed.Fault?.Code);
        Assert.Single(await groups.ListAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task AnUnknownOrEmptyGroupCannotChangeRouting()
    {
        using var bench = new Bench();
        var groups = new AddressGroupStore(bench.Db);
        var empty = await groups.AddAsync(new AddressGroup { Name = "empty" }, CancellationToken.None).ConfigureAwait(false);
        var unknown = await bench.Rules.AddAsync(RouteDefaults.Fresh("bad") with { Action = RouteAction.Block, Groups = [123] }, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal("empty-address-group", empty.Fault?.Code);
        Assert.Equal("unknown-address-group", unknown.Code);
        Assert.Empty(await bench.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task AnOlderRequestKeepsTheAddressGroupReferences()
    {
        using var bench = new Bench();
        var groups = new AddressGroupStore(bench.Db);
        var made = await groups.AddAsync(new AddressGroup { Name = "kept", Targets = ["192.0.2.1"] }, CancellationToken.None).ConfigureAwait(false);
        var added = await bench.Rules.AddAsync(RouteDefaults.Fresh("kept") with { Action = RouteAction.Block, Groups = [made.Record!.Id] }, CancellationToken.None).ConfigureAwait(false);
        var draft = RouteAnswers.Draft(new RouteRequest("kept", true, RouteAction.Block, "", [], [], "any"), added.Record);

        Assert.Equal([made.Record.Id], draft.Groups);
        var changed = await bench.Rules.ChangeAsync(added.Record!.Id, draft, CancellationToken.None).ConfigureAwait(false);
        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal([made.Record.Id], changed.Record!.Groups);
    }

    [Fact]
    public async Task AnExportMapsGroupNamesToTheDestinationServersIdentifiers()
    {
        using var source = new Bench();
        using var target = new Bench();
        var original = await source.Rules.ImportGroupAsync(new AddressGroup { Name = "video", Targets = ["192.0.2.0/24"] }, CancellationToken.None).ConfigureAwait(false);
        await target.Rules.ImportGroupAsync(new AddressGroup { Name = "unrelated", Targets = ["198.51.100.1"] }, CancellationToken.None).ConfigureAwait(false);
        await source.Rules.AddAsync(RouteDefaults.Fresh("video") with { Action = RouteAction.Block, Groups = [original.Record!.Id] }, CancellationToken.None).ConfigureAwait(false);
        var doc = await RoutingExport.TakeAsync(source.Rules, source.Presets, source.Geo, CancellationToken.None).ConfigureAwait(false);
        var read = RoutingFile.Read(RoutingExport.Text(doc));
        Assert.True(read.IsOk, read.Message);
        Assert.Equal(["video"], Assert.Single(read.Doc!.Rules).Groups);
        var plan = await RoutingImport.PlanAsync(read.Doc, target.Rules, target.Presets, target.Geo, target.Outbounds, target.Clients, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(["video"], plan.NewGroups);

        var answer = await RoutingImport.ApplyAsync(plan, target.Rules, target.Presets, target.Geo, CancellationToken.None).ConfigureAwait(false);

        Assert.Empty(answer.Refused);
        var transferred = Assert.Single(await target.Rules.GroupsAsync(CancellationToken.None).ConfigureAwait(false), group => group.Name == "video");
        Assert.NotEqual(original.Record.Id, transferred.Id);
        Assert.Equal([transferred.Id], Assert.Single(await target.Rules.ListAsync(CancellationToken.None).ConfigureAwait(false)).Groups);
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
        Assert.True(doc.Unified);
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
        Assert.Equal(["192.0.2.1"], read.Doc!.Basic.Direct);
        Assert.Empty(read.Doc.AddressGroups!);
        Assert.False(read.Doc.Unified);
    }

    private static RouteQuery Query(string address) => new() { Addresses = [IPAddress.Parse(address)], Port = 443 };
}
