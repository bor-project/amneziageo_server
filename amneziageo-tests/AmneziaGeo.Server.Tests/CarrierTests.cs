using AmneziaGeo.Server.Routing.Carrier;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Tests;

public class CarrierTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ABareNameTakesThePortOfTheServer()
    {
        var proxy = WsEndpoint.Parse("proxy.example.net", 443, "46.8.237.222");

        Assert.Equal("proxy.example.net", proxy.Host);
        Assert.Equal(443, proxy.Port);
        Assert.Equal(string.Empty, proxy.PathPrefix);
        Assert.Equal(string.Empty, proxy.Credentials);
    }

    [Fact]
    public void AnAddressCarriesPortPathAndCredentials()
    {
        var proxy = WsEndpoint.Parse("wss://user:secret@proxy.example.net:8443/token", 443, "46.8.237.222");

        Assert.Equal("proxy.example.net", proxy.Host);
        Assert.Equal(8443, proxy.Port);
        Assert.Equal("token", proxy.PathPrefix);
        Assert.Equal("user:secret", proxy.Credentials);
    }

    [Fact]
    public void AnUpgradeProvesTheKeysUnlessTheProxyTakesCredentials()
    {
        var plain = WsCarrier.Handshake(WsEndpoint.Parse("proxy.example.net", 443, "46.8.237.222"), 51820, "key", "AmneziaGeo token");
        var basic = WsCarrier.Handshake(
            WsEndpoint.Parse("wss://user:secret@proxy.example.net:8443/path", 443, "46.8.237.222"), 51820, "key", "AmneziaGeo token");
        var bare = WsCarrier.Handshake(WsEndpoint.Parse("proxy.example.net", 443, "46.8.237.222"), 51820, "key", null);

        Assert.StartsWith("GET /v1/events HTTP/1.1\r\n", plain, StringComparison.Ordinal);
        Assert.Contains("Authorization: AmneziaGeo token\r\n", plain, StringComparison.Ordinal);
        Assert.Contains("Authorization: Basic ", basic, StringComparison.Ordinal);
        Assert.DoesNotContain("AmneziaGeo", basic, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", bare, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyProxyFallsBackToTheServerItself()
    {
        var proxy = WsEndpoint.Parse(string.Empty, 51820, "46.8.237.222");

        Assert.Equal("46.8.237.222", proxy.Host);
        Assert.Equal(51820, proxy.Port);
    }

    [Fact]
    public void AWebsocketOutboundCarriesAnInterfaceAndAProxy()
    {
        Assert.True(OutboundKind.Known(OutboundKind.Ws));
        Assert.True(OutboundKind.HasLink(OutboundKind.Ws));
        Assert.True(OutboundKind.HasProxy(OutboundKind.Ws));
        Assert.False(OutboundKind.HasProxy(OutboundKind.Wg));
    }

    [Fact]
    public void AWebsocketOutboundWithoutAProxyIsRefused()
    {
        var fault = OutboundRules.Check(Carried() with { Proxy = string.Empty });

        Assert.Equal("bad-proxy", fault?.Code);
    }

    [Fact]
    public void AWebsocketOutboundWithAProxyHolds()
    {
        Assert.Null(OutboundRules.Check(Carried()));
    }

    [Fact]
    public void AProxyOnAnotherKindIsRefused()
    {
        var tunnel = Carried() with { Kind = OutboundKind.Wg };

        Assert.Equal("bad-proxy", OutboundRules.Check(tunnel)?.Code);
    }

    [Fact]
    public void AProxyOnTheHostItselfIsRefused()
    {
        var direct = OutboundDefaults.Direct() with { Proxy = "wss://proxy.example.net" };

        Assert.Equal("bad-kind", OutboundRules.Check(direct)?.Code);
    }

    [Fact]
    public void AProxyWithoutAPortIsRefused()
    {
        var fault = OutboundRules.Check(Carried() with { Proxy = "wss://proxy.example.net:0/token" });

        Assert.Equal("bad-proxy", fault?.Code);
    }

    [Fact]
    public void AFreshOutboundTakesTheKindItIsAskedFor()
    {
        Assert.Equal(OutboundKind.Ws, OutboundDefaults.Fresh("out1", OutboundKind.Ws).Kind);
        Assert.Equal(OutboundKind.Wg, OutboundDefaults.Fresh("out1", "nonsense").Kind);
    }

    [Fact]
    public void AWebsocketInterfaceMasqueradesLikeATunnel()
    {
        var links = OutboundRuleset.Links([Carried()], "eth0");

        Assert.Equal(["awgws"], links);
    }

    [Fact]
    public async Task TheSameProxyKeepsTheSameCarrier()
    {
        using var carriers = new CarrierHost();

        var first = await carriers.RaiseAsync(Carried(), CancellationToken.None);
        var again = await carriers.RaiseAsync(Carried(), CancellationToken.None);

        Assert.True(first > 0);
        Assert.Equal(first, again);
        Assert.Equal(first, carriers.PortOf("awgws"));
    }

    [Fact]
    public async Task AnotherProxyOpensAnotherCarrier()
    {
        using var carriers = new CarrierHost();

        var first = await carriers.RaiseAsync(Carried(), CancellationToken.None);
        var moved = await carriers.RaiseAsync(Carried() with { Proxy = "wss://127.0.0.2:8443/token" }, CancellationToken.None);

        Assert.NotEqual(first, moved);
        Assert.Equal(moved, carriers.PortOf("awgws"));
    }

    [Fact]
    public async Task ACarrierTakenDownLeavesNoPort()
    {
        using var carriers = new CarrierHost();
        await carriers.RaiseAsync(Carried(), CancellationToken.None);

        carriers.Withdraw("awgws");

        Assert.Equal(0, carriers.PortOf("awgws"));
    }

    [Fact]
    public async Task AWebsocketOutboundPointsItsPeerAtTheCarrier()
    {
        var ledger = new Ledger();
        var kernel = new Kernel();
        using var carriers = new CarrierHost();
        var host = new OutboundHost(ledger, kernel, new Clock(Now), carriers);

        await host.ApplyAsync(Carried(), CancellationToken.None);

        var peer = Assert.Single(Assert.Single(kernel.Updates).Peers);
        Assert.Equal(System.Net.IPAddress.Loopback, peer.Endpoint?.Address);
        Assert.Equal(carriers.PortOf("awgws"), peer.Endpoint?.Port);
    }

    [Fact]
    public async Task AWebsocketOutboundTurnedOffTakesItsCarrierDown()
    {
        var ledger = new Ledger();
        ledger.Links.Add("awgws");
        using var carriers = new CarrierHost();
        var host = new OutboundHost(ledger, new Kernel(), new Clock(Now), carriers);
        await host.ApplyAsync(Carried(), CancellationToken.None);

        await host.ApplyAsync(Carried() with { IsEnabled = false }, CancellationToken.None);

        Assert.Equal(0, carriers.PortOf("awgws"));
    }

    [Fact]
    public async Task AWebsocketOutboundWithoutCarriersSaysSo()
    {
        var host = new OutboundHost(new Ledger(), new Kernel(), new Clock(Now));

        var state = await host.ApplyAsync(Carried(), CancellationToken.None);

        Assert.False(state.HasLink);
        Assert.Contains("websocket", state.Fault, StringComparison.Ordinal);
    }

    private static OutboundConfig Carried() => new()
    {
        Name = "awgws",
        Kind = OutboundKind.Ws,
        IsEnabled = true,
        Host = "46.8.237.222",
        Port = 51821,
        Proxy = "wss://127.0.0.1:8443/token",
        PrivateKey = "OMMbTfBGZmzVOenkLmL2hFRuMbAG9pOZzTZ4L1H+wF0=",
        PeerKey = "eOWG0GXBLrpZOZ+FCwCJTvZ5rBEKHZ8NPvxNM3nJPl4=",
        Address = ["10.8.1.5/32"],
        Mtu = 1380,
        Keepalive = 25,
        Mark = OutboundRules.FirstMark,
        Table = OutboundRules.FirstTable,
    };
}
