using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public class EndpointKeeperTests
{
    [Fact]
    public async Task AnEndpointThatIsRaisedTakesItsClientsOnTop()
    {
        var (ledger, kernel, keeper) = Bench();
        var clients = new[] { Client(1, 1), Client(2, 1), Client(3, 1), Client(4, 2) };

        var sync = await keeper.RaiseAsync(Endpoint(1, "awg0"), clients, CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.True(ledger.Did("up awg0"));
        Assert.Equal([clients[0].PublicKey, clients[1].PublicKey, clients[2].PublicKey], Laid(kernel, "awg0"));
    }

    [Fact]
    public async Task AnEndpointThatIsTurnedOffTakesNoClients()
    {
        var (ledger, kernel, keeper) = Bench();
        ledger.Links.Add("awg0");

        var sync = await keeper.RaiseAsync(Endpoint(1, "awg0") with { IsEnabled = false }, [Client(1, 1)], CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.Equal(["remove awg0"], ledger.Steps);
        Assert.Empty(Laid(kernel, "awg0"));
    }

    [Fact]
    public async Task AnEndpointTheHostRefusedTakesNoClients()
    {
        var (ledger, kernel, keeper) = Bench();
        ledger.Refuses = "add";

        var sync = await keeper.RaiseAsync(Endpoint(1, "awg0"), [Client(1, 1)], CancellationToken.None);

        Assert.False(sync.IsDone);
        Assert.Empty(Laid(kernel, "awg0"));
    }

    [Fact]
    public async Task EveryEndpointIsRaisedWithItsOwnClientsAndTheRulesetIsLaidAfter()
    {
        var (ledger, kernel, keeper) = Bench();
        var clients = new[] { Client(1, 1), Client(2, 2) };

        var done = await keeper.SyncAsync([Endpoint(1, "awg0"), Endpoint(2, "awg1")], clients, CancellationToken.None);

        Assert.Equal(["awg0", "awg1", EndpointRuleset.TableName], done.Select(sync => sync.Name));
        Assert.Equal([clients[0].PublicKey], Laid(kernel, "awg0"));
        Assert.Equal([clients[1].PublicKey], Laid(kernel, "awg1"));
        Assert.Contains("table inet " + EndpointRuleset.TableName, ledger.Ruleset, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEndpointsThatAreOnAndWhoseInterfaceIsGoneOrDownAreLost()
    {
        var (ledger, _, keeper) = Bench();
        ledger.Links.UnionWith(["up", "down", "off"]);
        ledger.Down.Add("down");
        var configs = new[]
        {
            Endpoint(1, "up"),
            Endpoint(2, "gone"),
            Endpoint(3, "down"),
            Endpoint(4, "off") with { IsEnabled = false },
            Endpoint(5, "away") with { IsEnabled = false },
        };

        var lost = keeper.Lost(configs);

        Assert.Equal(["gone", "down"], lost.Select(config => config.Name));
    }

    [Fact]
    public async Task AGoneInterfaceIsLaidAgainWithItsKeysAddressesAndClients()
    {
        var (ledger, kernel, keeper) = Bench();
        var clients = new[] { Client(1, 1), Client(2, 1), Client(3, 1) with { IsEnabled = false }, Client(4, 2) };

        var mended = Assert.Single(await keeper.MendAsync([Endpoint(1, "awg0")], clients, CancellationToken.None));

        Assert.True(mended.Sync.IsDone);
        Assert.True(mended.WasGone);
        Assert.Equal(2, mended.Clients);
        Assert.True(ledger.Did("add awg0"));
        Assert.Equal("awg0", kernel.Updates[0].Name);
        Assert.Equal([clients[0].PublicKey, clients[1].PublicKey], Laid(kernel, "awg0"));
    }

    [Fact]
    public async Task ADownInterfaceIsBroughtUpAndKeepsWhatItCarries()
    {
        var (ledger, kernel, keeper) = Bench();
        ledger.Links.Add("awg0");
        ledger.Down.Add("awg0");

        var mended = Assert.Single(await keeper.MendAsync([Endpoint(1, "awg0")], [Client(1, 1)], CancellationToken.None));

        Assert.True(mended.Sync.IsDone);
        Assert.False(mended.WasGone);
        Assert.Equal(0, mended.Clients);
        Assert.False(ledger.Did("add awg0"));
        Assert.True(ledger.IsUp("awg0"));
        Assert.Empty(Laid(kernel, "awg0"));
    }

    [Fact]
    public async Task AnInterfaceTheHostStillRefusesIsReportedAndTakesNoClients()
    {
        var (ledger, kernel, keeper) = Bench();
        ledger.Refuses = "add";

        var mended = Assert.Single(await keeper.MendAsync([Endpoint(1, "awg0")], [Client(1, 1)], CancellationToken.None));

        Assert.False(mended.Sync.IsDone);
        Assert.True(mended.WasGone);
        Assert.Equal(0, mended.Clients);
        Assert.Empty(Laid(kernel, "awg0"));
    }

    [Fact]
    public async Task ASecondHoldWaitsUntilTheFirstIsLetGo()
    {
        var (_, _, keeper) = Bench();

        var first = await keeper.HoldAsync(CancellationToken.None);
        var second = keeper.HoldAsync(CancellationToken.None);
        await Task.Delay(50);
        var waited = !second.IsCompleted;
        first.Dispose();
        using var held = await second.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(waited);
    }

    private static (Ledger Ledger, Kernel Kernel, EndpointKeeper Keeper) Bench()
    {
        var ledger = new Ledger();
        var kernel = new Kernel();
        var clients = new ClientHost(ledger, kernel, new InterfaceFile(new InterfaceFileOptions { KeepFile = false }));

        return (ledger, kernel, new EndpointKeeper(ledger, new EndpointHost(ledger, kernel), clients));
    }

    private static string[] Laid(Kernel kernel, string name) =>
    [
        .. kernel.Updates
            .Where(update => update.Name == name)
            .SelectMany(update => update.Peers)
            .Where(peer => !peer.Remove)
            .Select(peer => peer.PublicKey)
    ];

    private static TunnelClient Client(long id, long configId) =>
        ClientDefaults.Fresh(configId, $"client{id}") with { Id = id, Address = [$"10.8.{configId}.{id + 1}/32"], IsEnabled = true };

    private static ServerConfig Endpoint(long id, string name) =>
        ConfigDefaults.Fresh(name) with { Id = id, Address = [$"10.8.{id}.1/24"], IsEnabled = true };
}
