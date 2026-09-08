using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Tests;

public class OutboundHostTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ATunnelIsRaisedInterfaceKeysAddressRouteAndRule()
    {
        var ledger = new Ledger();
        var kernel = new Kernel();
        var host = new OutboundHost(ledger, kernel, new Clock(Now));

        await host.ApplyAsync(Tunnel(), CancellationToken.None);

        Assert.Equal(
            ["add awgbor", "address awgbor 10.8.1.5/32", "up awgbor 1380", "route awgbor 42601", "rule 42497 42601 10000 True"],
            ledger.Steps);
        Assert.Equal("awgbor", Assert.Single(kernel.Updates).Name);
    }

    [Fact]
    public async Task AnInterfaceTheHostAlreadyCarriesIsNotAddedTwice()
    {
        var ledger = new Ledger();
        ledger.Links.Add("awgbor");
        var host = new OutboundHost(ledger, new Kernel(), new Clock(Now));

        await host.ApplyAsync(Tunnel(), CancellationToken.None);

        Assert.False(ledger.Did("add awgbor"));
        Assert.True(ledger.Did("route"));
    }

    [Fact]
    public async Task AnOutboundThatIsTurnedOffIsTakenOffTheHost()
    {
        var ledger = new Ledger();
        ledger.Links.Add("awgbor");
        var host = new OutboundHost(ledger, new Kernel(), new Clock(Now));

        var state = await host.ApplyAsync(Tunnel() with { IsEnabled = false }, CancellationToken.None);

        Assert.Equal(["rule 42497 42601 10000 False", "clear 42601", "remove awgbor"], ledger.Steps);
        Assert.False(state.HasLink);
    }

    [Fact]
    public async Task AnOutboundThroughTheHostItselfOnlyTakesTheRule()
    {
        var ledger = new Ledger();
        var host = new OutboundHost(ledger, new Kernel(), new Clock(Now));

        var state = await host.ApplyAsync(OutboundDefaults.Direct(), CancellationToken.None);

        Assert.Equal(["rule 42497 254 10000 True"], ledger.Steps);
        Assert.True(state.IsAlive);
    }

    [Fact]
    public async Task AHostThatRefusesAStepLeavesTheReasonOnTheOutbound()
    {
        var ledger = new Ledger { Refuses = "route" };
        var host = new OutboundHost(ledger, new Kernel(), new Clock(Now));

        var state = await host.ApplyAsync(Tunnel(), CancellationToken.None);

        Assert.False(state.HasLink);
        Assert.Contains("route awgbor", state.Fault, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PuttingEveryOutboundOnTheHostEndsWithTheFirewall()
    {
        var ledger = new Ledger();
        var host = new OutboundHost(ledger, new Kernel(), new Clock(Now));

        var states = await host.SyncAsync([OutboundDefaults.Direct(), Tunnel()], CancellationToken.None);

        Assert.Equal(2, states.Count);
        Assert.Equal("firewall", ledger.Steps[^1]);
        Assert.Contains("oifname \"awgbor\" masquerade", ledger.Ruleset, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStateOfATunnelIsReadOffTheInterface()
    {
        var kernel = new Kernel();
        kernel.Hold(new AwgDevice
        {
            Name = "awgbor",
            Peers =
            [
                new AwgPeer
                {
                    PublicKey = "eOWG0GXBLrpZOZ+FCwCJTvZ5rBEKHZ8NPvxNM3nJPl4=",
                    Endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("46.8.237.222"), 51821),
                    LastHandshake = Now.AddMinutes(-1),
                    RxBytes = 4096,
                    TxBytes = 2048,
                },
            ],
        });
        var host = new OutboundHost(new Ledger(), kernel, new Clock(Now));

        var state = host.State(Tunnel());

        Assert.True(state.HasLink);
        Assert.True(state.IsAlive);
        Assert.Equal("46.8.237.222:51821", state.Endpoint);
        Assert.Equal(4096ul, state.RxBytes);
        Assert.Equal(2048ul, state.TxBytes);
    }

    [Fact]
    public void AnInterfaceTheHostDoesNotCarryReadsAsMissing()
    {
        var host = new OutboundHost(new Ledger(), new Kernel(), new Clock(Now));

        var state = host.State(Tunnel());

        Assert.False(state.HasLink);
        Assert.False(state.IsAlive);
        Assert.Null(state.LastHandshake);
    }

    private static OutboundConfig Tunnel() => new()
    {
        Name = "awgbor",
        Kind = OutboundKind.Wg,
        IsEnabled = true,
        Host = "46.8.237.222",
        Port = 51821,
        PrivateKey = "OMMbTfBGZmzVOenkLmL2hFRuMbAG9pOZzTZ4L1H+wF0=",
        PeerKey = "eOWG0GXBLrpZOZ+FCwCJTvZ5rBEKHZ8NPvxNM3nJPl4=",
        Address = ["10.8.1.5/32"],
        Mtu = 1380,
        Keepalive = 25,
        Mark = OutboundRules.FirstMark,
        Table = OutboundRules.FirstTable,
    };
}
