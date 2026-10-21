using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Balance;

namespace AmneziaGeo.Server.Tests;

public class BalanceHoldersTests
{
    [Fact]
    public async Task AChannelComesLastInTheNamedBalancersAndLeavesTheRest()
    {
        using var bench = new Bench();
        var eu = await GroupAsync(bench, "eu", "k1", "k2");
        var home = await GroupAsync(bench, "home", "k2");

        var refused = await bench.Balancers.RefuseHoldersAsync("k1", [home], CancellationToken.None);
        await bench.Balancers.HoldAsync("k1", [home], CancellationToken.None);

        Assert.Null(refused);
        Assert.Equal(["k2"], await MembersAsync(bench, eu));
        Assert.Equal(["k2", "k1"], await MembersAsync(bench, home));
    }

    [Fact]
    public async Task ABalancerThatKeepsTheChannelKeepsItsOrderAndTime()
    {
        using var bench = new Bench();
        var eu = await GroupAsync(bench, "eu", "k1", "k2");
        var held = await bench.Balancers.FindAsync(eu, CancellationToken.None);
        bench.Clock.Pass(TimeSpan.FromHours(1));

        await bench.Balancers.HoldAsync("k1", [eu], CancellationToken.None);
        var read = await bench.Balancers.FindAsync(eu, CancellationToken.None);

        Assert.Equal(["k1", "k2"], read!.Members);
        Assert.Equal(held!.UpdatedUtc, read.UpdatedUtc);
    }

    [Fact]
    public async Task TheLastChannelOfABalancerStaysInIt()
    {
        using var bench = new Bench();
        var solo = await GroupAsync(bench, "solo", "k1");

        var refused = await bench.Balancers.RefuseHoldersAsync("k1", [], CancellationToken.None);

        Assert.Equal("no-members", refused!.Code);
        Assert.Equal(["k1"], await MembersAsync(bench, solo));
    }

    [Fact]
    public async Task AnUnknownBalancerIsRefused()
    {
        using var bench = new Bench();
        await GroupAsync(bench, "eu", "k2");

        var refused = await bench.Balancers.RefuseHoldersAsync("k1", [999], CancellationToken.None);

        Assert.Equal("unknown-balancer", refused!.Code);
    }

    [Fact]
    public async Task AFullBalancerTakesNoMoreChannels()
    {
        using var bench = new Bench();
        var full = await GroupAsync(bench, "full", [.. Enumerable.Range(1, BalanceRules.MaxMembers).Select(at => $"k{at}")]);

        var refused = await bench.Balancers.RefuseHoldersAsync("k99", [full], CancellationToken.None);

        Assert.Equal("too-many-members", refused!.Code);
    }

    private static async Task<long> GroupAsync(Bench bench, string name, params string[] members) =>
        (await bench.Balancers.AddAsync(BalanceDefaults.Fresh(name) with { Members = members }, CancellationToken.None)).Record!.Id;

    private static async Task<IReadOnlyList<string>> MembersAsync(Bench bench, long id) =>
        (await bench.Balancers.FindAsync(id, CancellationToken.None))!.Members;
}
