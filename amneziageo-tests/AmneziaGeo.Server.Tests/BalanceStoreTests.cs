using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class BalanceStoreTests
{
    [Fact]
    public async Task AFreshInstallCarriesNoBalancers()
    {
        using var bench = new Bench();

        Assert.Empty(await bench.Balancers.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ABalancerTakesThePlaceAtTheEnd()
    {
        using var bench = new Bench();

        var first = await bench.Balancers.AddAsync(Group("home"), CancellationToken.None);
        var second = await bench.Balancers.AddAsync(Group("abroad"), CancellationToken.None);

        Assert.True(first.IsOk, first.Message);
        Assert.Equal(1, first.Record!.Position);
        Assert.Equal(2, second.Record!.Position);
    }

    [Fact]
    public async Task ABalancerUnderTheNameOfAnOutboundIsRefused()
    {
        using var bench = new Bench();

        var added = await bench.Balancers.AddAsync(Group("direct"), CancellationToken.None);

        Assert.Equal(BalanceOutcome.NameTaken, added.Outcome);
    }

    [Fact]
    public async Task ASettingOutOfShapeIsRefusedWithItsCode()
    {
        using var bench = new Bench();

        var added = await bench.Balancers.AddAsync(
            Group("home") with { Strategy = "sideways" },
            CancellationToken.None);

        Assert.Equal(BalanceOutcome.Invalid, added.Outcome);
        Assert.Equal("bad-strategy", added.Code);
    }

    [Fact]
    public async Task ABalancerIsReadBackAsItWasSaved()
    {
        using var bench = new Bench();
        var added = await bench.Balancers.AddAsync(
            Group("home") with { Strategy = BalanceStrategy.Sticky, Members = ["direct", "awgbor"] },
            CancellationToken.None);

        var held = await bench.Balancers.FindAsync(added.Record!.Id, CancellationToken.None);

        Assert.Equal(BalanceStrategy.Sticky, held?.Strategy);
        Assert.Equal(["direct", "awgbor"], held?.Members);
        Assert.True(held?.IsEnabled);
    }

    [Fact]
    public async Task ABalancerIsTurnedOffAndBackOn()
    {
        using var bench = new Bench();
        var added = await bench.Balancers.AddAsync(Group("home"), CancellationToken.None);

        var off = await bench.Balancers.SwitchAsync(added.Record!.Id, false, CancellationToken.None);
        var on = await bench.Balancers.SwitchAsync(added.Record.Id, true, CancellationToken.None);

        Assert.False(off.Record!.IsEnabled);
        Assert.True(on.Record!.IsEnabled);
    }

    [Fact]
    public async Task ABalancerARuleLeavesThroughStays()
    {
        using var bench = new Bench();
        var added = await bench.Balancers.AddAsync(Group("home"), CancellationToken.None);
        await bench.Rules.AddAsync(
            RouteDefaults.Fresh("ru") with { Outbound = "home", Targets = ["geoip:ru"] },
            CancellationToken.None);

        var gone = await bench.Balancers.RemoveAsync(added.Record!.Id, CancellationToken.None);

        Assert.Equal("balancer-in-use", gone.Code);
        Assert.Single(await bench.Balancers.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ABalancerNoRuleNamesIsRemoved()
    {
        using var bench = new Bench();
        var added = await bench.Balancers.AddAsync(Group("home"), CancellationToken.None);

        var gone = await bench.Balancers.RemoveAsync(added.Record!.Id, CancellationToken.None);

        Assert.True(gone.IsOk, gone.Message);
        Assert.Empty(await bench.Balancers.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ABalancerThePanelDoesNotHoldIsNamedInTheRefusal()
    {
        using var bench = new Bench();

        var changed = await bench.Balancers.ChangeAsync(7, Group("home"), CancellationToken.None);

        Assert.Equal(BalanceOutcome.Unknown, changed.Outcome);
        Assert.Equal("unknown-balancer", changed.Code);
    }

    private static Balancer Group(string name) => BalanceDefaults.Fresh(name) with { Members = ["direct"] };
}
