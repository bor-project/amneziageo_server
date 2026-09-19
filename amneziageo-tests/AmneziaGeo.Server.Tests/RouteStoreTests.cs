using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class RouteStoreTests
{
    [Fact]
    public async Task AFreshInstallCarriesNoRules()
    {
        using var bench = new Bench();

        Assert.Empty(await bench.Rules.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ARuleTakesThePlaceAtTheEnd()
    {
        using var bench = new Bench();

        var first = await bench.Rules.AddAsync(Rule("ru through the tunnel"), CancellationToken.None);
        var second = await bench.Rules.AddAsync(Rule("ads nowhere"), CancellationToken.None);

        Assert.True(first.IsOk, first.Message);
        Assert.Equal(1, first.Record!.Position);
        Assert.Equal(2, second.Record!.Position);
    }

    [Fact]
    public async Task ARuleUnderATakenNameIsRefused()
    {
        using var bench = new Bench();
        await bench.Rules.AddAsync(Rule("same"), CancellationToken.None);

        var again = await bench.Rules.AddAsync(Rule("same"), CancellationToken.None);

        Assert.Equal(RouteOutcome.NameTaken, again.Outcome);
    }

    [Fact]
    public async Task ARuleWithBrokenSettingsIsRefusedByCode()
    {
        using var bench = new Bench();

        var added = await bench.Rules.AddAsync(Rule("bad") with { Targets = ["what is this"] }, CancellationToken.None);

        Assert.Equal(RouteOutcome.Invalid, added.Outcome);
        Assert.Equal("bad-target", added.Code);
    }

    [Fact]
    public async Task ChangingARuleKeepsItsPlace()
    {
        using var bench = new Bench();
        await bench.Rules.AddAsync(Rule("first"), CancellationToken.None);
        var second = await bench.Rules.AddAsync(Rule("second"), CancellationToken.None);

        var changed = await bench.Rules.ChangeAsync(
            second.Record!.Id,
            Rule("second") with { Targets = ["geosite:youtube"], Ports = ["443"] },
            CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal(2, changed.Record!.Position);
        Assert.Equal(["geosite:youtube"], changed.Record.Targets);
        Assert.Equal(["443"], changed.Record.Ports);
    }

    [Fact]
    public async Task ARuleIsTurnedOffAndOnAgain()
    {
        using var bench = new Bench();
        var added = await bench.Rules.AddAsync(Rule("first"), CancellationToken.None);

        var off = await bench.Rules.SwitchAsync(added.Record!.Id, false, CancellationToken.None);
        var on = await bench.Rules.SwitchAsync(added.Record.Id, true, CancellationToken.None);

        Assert.False(off.Record!.IsEnabled);
        Assert.True(on.Record!.IsEnabled);
    }

    [Fact]
    public async Task MovingARuleSwapsItWithItsNeighbourAndStopsAtTheEdge()
    {
        using var bench = new Bench();
        var first = await bench.Rules.AddAsync(Rule("first"), CancellationToken.None);
        var second = await bench.Rules.AddAsync(Rule("second"), CancellationToken.None);

        var moved = await bench.Rules.MoveAsync(second.Record!.Id, true, CancellationToken.None);
        var again = await bench.Rules.MoveAsync(moved.Record!.Id, true, CancellationToken.None);
        var held = await bench.Rules.ListAsync(CancellationToken.None);

        Assert.Equal(1, again.Record!.Position);
        Assert.Equal(["second", "first"], held.Select(rule => rule.Name));
        Assert.Equal(first.Record!.Id, held[1].Id);
    }

    [Fact]
    public async Task ARuleIsRemoved()
    {
        using var bench = new Bench();
        var added = await bench.Rules.AddAsync(Rule("first"), CancellationToken.None);

        var gone = await bench.Rules.RemoveAsync(added.Record!.Id, CancellationToken.None);

        Assert.True(gone.IsOk, gone.Message);
        Assert.Empty(await bench.Rules.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ACommandOnARuleThePanelDoesNotHoldIsRefused()
    {
        using var bench = new Bench();

        Assert.Equal(RouteOutcome.Unknown, (await bench.Rules.ChangeAsync(7, Rule("x"), CancellationToken.None)).Outcome);
        Assert.Equal(RouteOutcome.Unknown, (await bench.Rules.SwitchAsync(7, true, CancellationToken.None)).Outcome);
        Assert.Equal(RouteOutcome.Unknown, (await bench.Rules.MoveAsync(7, true, CancellationToken.None)).Outcome);
        Assert.Equal(RouteOutcome.Unknown, (await bench.Rules.RemoveAsync(7, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task ARuleIsPutAtAPlaceAndTheOthersCloseUp()
    {
        using var bench = new Bench();
        await bench.Rules.AddAsync(Rule("first"), CancellationToken.None);
        await bench.Rules.AddAsync(Rule("second"), CancellationToken.None);
        var third = await bench.Rules.AddAsync(Rule("third"), CancellationToken.None);

        var placed = await bench.Rules.PlaceAsync(third.Record!.Id, 1, CancellationToken.None);
        var held = await bench.Rules.ListAsync(CancellationToken.None);

        Assert.True(placed.IsOk, placed.Message);
        Assert.Equal(["third", "first", "second"], held.Select(rule => rule.Name));
        Assert.Equal([1, 2, 3], held.Select(rule => rule.Position));
    }

    [Fact]
    public async Task APlaceBeyondTheEndPutsTheRuleLast()
    {
        using var bench = new Bench();
        var first = await bench.Rules.AddAsync(Rule("first"), CancellationToken.None);
        await bench.Rules.AddAsync(Rule("second"), CancellationToken.None);

        await bench.Rules.PlaceAsync(first.Record!.Id, 99, CancellationToken.None);

        Assert.Equal(["second", "first"], (await bench.Rules.ListAsync(CancellationToken.None)).Select(rule => rule.Name));
        Assert.Equal(RouteOutcome.Unknown, (await bench.Rules.PlaceAsync(77, 1, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task TheClientsTheInterfacesAndTheSourcePortsOfARuleAreKept()
    {
        using var bench = new Bench();
        var draft = Rule("first") with { Clients = ["bor", "guest"], Inbounds = ["awg1"], SourcePorts = ["5000-6000"] };

        var added = await bench.Rules.AddAsync(draft, CancellationToken.None);
        var held = await bench.Rules.FindAsync(added.Record!.Id, CancellationToken.None);

        Assert.Equal(["bor", "guest"], held!.Clients);
        Assert.Equal(["awg1"], held.Inbounds);
        Assert.Equal(["5000-6000"], held.SourcePorts);
    }

    [Fact]
    public async Task TheBasicListsAreEmptyUntilSavedAndKeptAfter()
    {
        using var bench = new Bench();
        Assert.Empty((await bench.Rules.ReadBasicAsync(CancellationToken.None)).Direct);

        var fault = await bench.Rules.SaveBasicAsync(
            new RouteBasic { Direct = ["geoip:ru", "ya.ru"], Block = ["geosite:category-ads-all"] },
            CancellationToken.None);
        var held = await bench.Rules.ReadBasicAsync(CancellationToken.None);

        Assert.Null(fault);
        Assert.Equal(["geoip:ru", "ya.ru"], held.Direct);
        Assert.Equal(["geosite:category-ads-all"], held.Block);
    }

    [Fact]
    public async Task ABasicListWithABrokenTargetIsRefusedAndNothingIsSaved()
    {
        using var bench = new Bench();

        var fault = await bench.Rules.SaveBasicAsync(new RouteBasic { Block = ["what is this"] }, CancellationToken.None);

        Assert.Equal("bad-target", fault?.Code);
        Assert.Empty((await bench.Rules.ReadBasicAsync(CancellationToken.None)).Block);
    }

    private static RouteRule Rule(string name) => RouteDefaults.Fresh(name) with
    {
        Outbound = "direct",
        Targets = ["geoip:ru"],
    };
}
