using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class RuleNamingTests
{
    [Fact]
    public async Task AClientIsNamedByTheListedRulesOnly()
    {
        using var bench = new Bench();
        var both = await RuleAsync(bench, "both", clients: ["anna", "bob"]);
        var single = await RuleAsync(bench, "single", clients: ["bob"]);
        var anyone = await RuleAsync(bench, "anyone");

        await bench.Rules.NameInAsync(RuleSide.Clients, "carl", [both], CancellationToken.None);
        await bench.Rules.NameInAsync(RuleSide.Clients, "bob", [single], CancellationToken.None);

        Assert.Equal(["anna", "carl"], await ClientsOfAsync(bench, both));
        Assert.Equal(["bob"], await ClientsOfAsync(bench, single));
        Assert.Empty(await ClientsOfAsync(bench, anyone));
    }

    [Fact]
    public async Task ARuleForEveryClientIsNotNarrowed()
    {
        using var bench = new Bench();
        var anyone = await RuleAsync(bench, "anyone");

        var refused = await bench.Rules.RefuseNamingAsync(RuleSide.Clients, "carl", [anyone], CancellationToken.None);

        Assert.Equal("rule-for-everyone", refused!.Code);
    }

    [Fact]
    public async Task TheLastClientOfARuleStaysInIt()
    {
        using var bench = new Bench();
        var both = await RuleAsync(bench, "both", clients: ["anna", "bob"]);
        await RuleAsync(bench, "single", clients: ["bob"]);

        var refused = await bench.Rules.RefuseNamingAsync(RuleSide.Clients, "bob", [both], CancellationToken.None);
        var kept = await bench.Rules.RefuseNamingAsync(RuleSide.Clients, "anna", [], CancellationToken.None);

        Assert.Equal("rule-keeps-last", refused!.Code);
        Assert.Null(kept);
    }

    [Fact]
    public async Task AnUnknownRuleIsRefused()
    {
        using var bench = new Bench();

        var refused = await bench.Rules.RefuseNamingAsync(RuleSide.Clients, "anna", [999], CancellationToken.None);

        Assert.Equal("unknown-rule", refused!.Code);
    }

    [Fact]
    public async Task ClientsAreNamedWhateverTheCaseAndAnInterfaceComesLast()
    {
        using var bench = new Bench();
        var both = await RuleAsync(bench, "both", clients: ["Anna", "bob"], inbounds: ["awg3", "awg1"]);

        await bench.Rules.NameInAsync(RuleSide.Clients, "anna", [], CancellationToken.None);
        await bench.Rules.NameInAsync(RuleSide.Inbounds, "awg2", [both], CancellationToken.None);
        var read = await bench.Rules.FindAsync(both, CancellationToken.None);

        Assert.Equal(["bob"], read!.Clients);
        Assert.Equal(["awg3", "awg1", "awg2"], read.Inbounds);
    }

    [Fact]
    public async Task ARemovedClientLeavesItsRulesAndTurnsOffARuleItWasLastIn()
    {
        using var bench = new Bench();
        await ClientAsync(bench, "anna", "awg1");
        var bob = await ClientAsync(bench, "bob", "awg1");
        var both = await RuleAsync(bench, "both", clients: ["anna", "Bob"]);
        var single = await RuleAsync(bench, "single", clients: ["bob"]);

        await bench.Clients.RemoveAsync(bob, CancellationToken.None);
        var left = await bench.Rules.FindAsync(both, CancellationToken.None);
        var off = await bench.Rules.FindAsync(single, CancellationToken.None);

        Assert.Equal(["anna"], left!.Clients);
        Assert.True(left.IsEnabled);
        Assert.Equal(["bob"], off!.Clients);
        Assert.False(off.IsEnabled);
    }

    [Fact]
    public async Task ARemovedInterfaceTakesItselfAndItsClientsOffTheRules()
    {
        using var bench = new Bench();
        await ClientAsync(bench, "anna", "awg1");
        var awg1 = (await bench.Configs.ListAsync(CancellationToken.None))[0].Id;
        var clients = await RuleAsync(bench, "clients", clients: ["anna", "bob"]);
        var alone = await RuleAsync(bench, "alone", inbounds: ["awg1"]);
        var shared = await RuleAsync(bench, "shared", inbounds: ["awg1", "awg2"]);

        await bench.Configs.RemoveAsync(awg1, CancellationToken.None);
        var named = await bench.Rules.FindAsync(clients, CancellationToken.None);
        var off = await bench.Rules.FindAsync(alone, CancellationToken.None);
        var left = await bench.Rules.FindAsync(shared, CancellationToken.None);

        Assert.Equal(["bob"], named!.Clients);
        Assert.Equal(["awg1"], off!.Inbounds);
        Assert.False(off.IsEnabled);
        Assert.Equal(["awg2"], left!.Inbounds);
        Assert.True(left.IsEnabled);
    }

    private static async Task<long> RuleAsync(
        Bench bench,
        string name,
        IReadOnlyList<string>? clients = null,
        IReadOnlyList<string>? inbounds = null)
    {
        var added = await bench.Rules.AddAsync(
            RouteDefaults.Fresh(name) with
            {
                Action = RouteAction.Direct,
                Targets = ["geoip:ru"],
                Clients = clients ?? [],
                Inbounds = inbounds ?? [],
            },
            CancellationToken.None);

        Assert.True(added.IsOk, added.Message);

        return added.Record!.Id;
    }

    private static async Task<IReadOnlyList<string>> ClientsOfAsync(Bench bench, long rule) =>
        (await bench.Rules.FindAsync(rule, CancellationToken.None))!.Clients;

    private static async Task<long> ClientAsync(Bench bench, string name, string endpoint)
    {
        var endpoints = await bench.Configs.ListAsync(CancellationToken.None);
        var config = endpoints.FirstOrDefault(one => one.Name == endpoint)?.Id
            ?? (await bench.Configs.AddAsync(ConfigDefaults.Fresh(endpoint), CancellationToken.None)).Record!.Id;
        var held = await bench.Clients.ListAsync(CancellationToken.None);
        var added = await bench.Clients.AddAsync(
            ClientDefaults.Fresh(config, name) with { Address = [$"10.8.0.{held.Count + 2}/32"] },
            CancellationToken.None);

        Assert.True(added.IsOk, added.Message);

        return added.Record!.Id;
    }
}
