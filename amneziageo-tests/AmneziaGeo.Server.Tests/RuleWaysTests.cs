using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class RuleWaysTests
{
    [Fact]
    public async Task TheListedRulesLeaveThroughTheWay()
    {
        using var bench = new Bench();
        var first = await RuleAsync(bench, "first", "k1");
        var second = await RuleAsync(bench, "second", "k2");

        var refused = await bench.Rules.RefuseLeavingAsync("k2", [first, second], CancellationToken.None);
        await bench.Rules.LeaveThroughAsync("k2", [first, second], CancellationToken.None);

        Assert.Null(refused);
        Assert.Equal("k2", (await bench.Rules.FindAsync(first, CancellationToken.None))!.Outbound);
        Assert.Equal("k2", (await bench.Rules.FindAsync(second, CancellationToken.None))!.Outbound);
    }

    [Fact]
    public async Task ARuleLeavingThroughTheWayStaysOnTheList()
    {
        using var bench = new Bench();
        await RuleAsync(bench, "first", "k1");

        var refused = await bench.Rules.RefuseLeavingAsync("k1", [], CancellationToken.None);

        Assert.Equal("way-keeps-rules", refused!.Code);
    }

    [Fact]
    public async Task ARuleThatSendsNothingOutIsRefused()
    {
        using var bench = new Bench();
        var first = await RuleAsync(bench, "first", "k1");
        var direct = await RuleAsync(bench, "direct", null);

        var refused = await bench.Rules.RefuseLeavingAsync("k1", [first, direct], CancellationToken.None);

        Assert.Equal("rule-not-out", refused!.Code);
    }

    [Fact]
    public async Task AnUnknownRuleIsRefused()
    {
        using var bench = new Bench();

        var refused = await bench.Rules.RefuseLeavingAsync("k1", [999], CancellationToken.None);

        Assert.Equal("unknown-rule", refused!.Code);
    }

    private static async Task<long> RuleAsync(Bench bench, string name, string? way)
    {
        var added = await bench.Rules.AddAsync(
            RouteDefaults.Fresh(name) with
            {
                Action = way is null ? RouteAction.Direct : RouteAction.Out,
                Outbound = way ?? string.Empty,
                Targets = ["geoip:ru"],
            },
            CancellationToken.None);

        Assert.True(added.IsOk, added.Message);

        return added.Record!.Id;
    }
}
