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

    private static RouteRule Rule(string name) => RouteDefaults.Fresh(name) with
    {
        Outbound = "direct",
        Targets = ["geoip:ru"],
    };
}
