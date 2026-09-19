using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public class EndpointHostTests
{
    [Fact]
    public async Task AnEndpointIsRaisedForwardingInterfaceKeysAddressAndUp()
    {
        var ledger = new Ledger();
        var kernel = new Kernel();
        var host = new EndpointHost(ledger, kernel);

        var sync = await host.ApplyAsync(Endpoint(), CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.Equal(
            ["forwarding", "add awg0", "address awg0 10.0.0.1/24 fd42:6d79:7670::cafe:1/112", "up awg0 1360"],
            ledger.Steps);
        Assert.Equal("awg0", Assert.Single(kernel.Updates).Name);
    }

    [Fact]
    public async Task TheKernelTakesThePortTheKeyAndTheWholeObfuscation()
    {
        var kernel = new Kernel();
        var host = new EndpointHost(new Ledger(), kernel);

        await host.ApplyAsync(Endpoint(), CancellationToken.None);

        var update = Assert.Single(kernel.Updates);
        Assert.Equal(51820, (int)update.ListenPort!.Value);
        Assert.Equal(Key, update.PrivateKey);
        Assert.Equal(194488238u, update.Obfuscation!.H1.Low);
        Assert.Equal(194553774u, update.Obfuscation.H1.High);
        Assert.Equal("wHS9TkIlE61wUO3AglOpnvrCIWDQ5vmoa20cbOEW56k=", update.Obfuscation.HeaderProtectionKey);
    }

    [Fact]
    public async Task AnInterfaceTheHostAlreadyCarriesIsNotAddedTwice()
    {
        var ledger = new Ledger();
        ledger.Links.Add("awg0");
        var host = new EndpointHost(ledger, new Kernel());

        await host.ApplyAsync(Endpoint(), CancellationToken.None);

        Assert.False(ledger.Did("add awg0"));
        Assert.True(ledger.Did("up awg0"));
    }

    [Fact]
    public async Task AnEndpointThatIsTurnedOffIsTakenOffTheHost()
    {
        var ledger = new Ledger();
        ledger.Links.Add("awg0");
        var host = new EndpointHost(ledger, new Kernel());

        var sync = await host.ApplyAsync(Endpoint() with { IsEnabled = false }, CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.Equal(["remove awg0"], ledger.Steps);
    }

    [Fact]
    public async Task AHostThatRefusesIsReportedAndNotThrown()
    {
        var ledger = new Ledger { Refuses = "add" };
        var host = new EndpointHost(ledger, new Kernel());

        var sync = await host.ApplyAsync(Endpoint(), CancellationToken.None);

        Assert.False(sync.IsDone);
        Assert.Contains("add awg0", sync.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRulesetMasqueradesBothFamiliesAndClosesThePrivateRanges()
    {
        var ledger = new Ledger { Uplink = "ens3" };
        var host = new EndpointHost(ledger, new Kernel());

        await host.SyncAsync([Endpoint()], [], CancellationToken.None);

        Assert.Contains("ip saddr 10.0.0.0/24 oifname \"ens3\" masquerade", ledger.Ruleset, StringComparison.Ordinal);
        Assert.Contains(
            "ip6 saddr fd42:6d79:7670::cafe:0/112 oifname \"ens3\" masquerade",
            ledger.Ruleset,
            StringComparison.Ordinal);
        Assert.Contains("ct state established,related accept", ledger.Ruleset, StringComparison.Ordinal);
        Assert.Contains("iifname \"awg0\" ip daddr 192.168.0.0/16 reject", ledger.Ruleset, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEndpointWithoutNatIsNotMasqueraded()
    {
        var text = EndpointRuleset.Text([Endpoint() with { Nat = false }], [], "ens3");

        Assert.DoesNotContain("masquerade", text, StringComparison.Ordinal);
        Assert.Contains("oifname \"awg0\" drop", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEndpointThatIsTurnedOffIsLeftOutOfTheRuleset()
    {
        var text = EndpointRuleset.Text([Endpoint() with { IsEnabled = false }], [], "ens3");

        Assert.DoesNotContain("awg0", text, StringComparison.Ordinal);
        Assert.Contains("table inet amneziageo_in", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostWithNoUplinkMasqueradesNothing()
    {
        var text = EndpointRuleset.Text([Endpoint()], [], string.Empty);

        Assert.DoesNotContain("masquerade", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AClientThatTakesTheTunnelIsReachedWhileTheOthersAreNot()
    {
        var text = EndpointRuleset.Text(
            [Endpoint() with { Id = 7 }],
            [
                Client() with { Address = ["10.0.0.5/32"], Inbound = ClientInbound.Network },
                Client() with { Address = ["10.0.0.6/32"], Inbound = ClientInbound.Server },
                Client() with { Address = ["10.0.0.7/32"], Inbound = ClientInbound.Off },
            ],
            "ens3");

        Assert.Contains("elements = { 10.0.0.5/32 }", text, StringComparison.Ordinal);
        Assert.Contains("oifname \"awg0\" ip daddr @in7v4 accept", text, StringComparison.Ordinal);
        Assert.Contains("oifname \"awg0\" drop", text, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.6", text, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.7", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNetworksBehindAClientAreReachedTogetherWithIt()
    {
        var text = EndpointRuleset.Text(
            [Endpoint() with { Id = 7 }],
            [
                Client() with
                {
                    Address = ["10.0.0.5/32", "fd42:6d79:7670::cafe:5/128"],
                    Routes = ["192.168.88.0/24"],
                    Inbound = ClientInbound.Network,
                },
            ],
            "ens3");

        Assert.Contains("elements = { 10.0.0.5/32, 192.168.88.0/24 }", text, StringComparison.Ordinal);
        Assert.Contains("elements = { fd42:6d79:7670::cafe:5/128 }", text, StringComparison.Ordinal);
        Assert.Contains("oifname \"awg0\" ip6 daddr @in7v6 accept", text, StringComparison.Ordinal);
    }

    [Fact]
    public void APortOfTheHostIsCarriedToTheClientOverBothFamilies()
    {
        var text = EndpointRuleset.Text(
            [Endpoint() with { Id = 7 }],
            [
                Client() with
                {
                    Address = ["10.0.0.5/32", "fd42:6d79:7670::cafe:5/128"],
                    Forwards = [new PortForward("tcp", 2222, 22)],
                },
            ],
            "ens3");

        Assert.Contains(
            "iifname \"ens3\" meta nfproto ipv4 tcp dport 2222 dnat ip to 10.0.0.5:22",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "iifname \"ens3\" meta nfproto ipv6 tcp dport 2222 dnat ip6 to [fd42:6d79:7670::cafe:5]:22",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "oifname \"awg0\" ip daddr 10.0.0.5 tcp dport 22 accept",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AClientTakesACarriedPortUnderTheAddressOfTheEndpoint()
    {
        var text = EndpointRuleset.Text(
            [Endpoint() with { Id = 7 }],
            [
                Client() with
                {
                    Address = ["10.0.0.5/32", "fd42:6d79:7670::cafe:5/128"],
                    Forwards = [new PortForward("tcp", 2222, 22)],
                },
            ],
            "ens3");

        Assert.Contains(
            "oifname \"awg0\" ip daddr 10.0.0.5 tcp dport 22 ct status dnat masquerade",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "oifname \"awg0\" ip6 daddr fd42:6d79:7670::cafe:5 tcp dport 22 ct status dnat masquerade",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AClientThatIsTurnedOffTakesNeitherTheTunnelNorAPortOfTheHost()
    {
        var text = EndpointRuleset.Text(
            [Endpoint() with { Id = 7 }],
            [
                Client() with
                {
                    Address = ["10.0.0.5/32"],
                    Inbound = ClientInbound.Network,
                    Forwards = [new PortForward("tcp", 2222, 22)],
                    IsEnabled = false,
                },
            ],
            "ens3");

        Assert.DoesNotContain("10.0.0.5", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dnat", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostWithNoUplinkCarriesNoPortToAClient()
    {
        var text = EndpointRuleset.Text(
            [Endpoint() with { Id = 7 }],
            [Client() with { Address = ["10.0.0.5/32"], Forwards = [new PortForward("udp", 5353, 53)] }],
            string.Empty);

        Assert.DoesNotContain("dnat", text, StringComparison.Ordinal);
        Assert.DoesNotContain("masquerade", text, StringComparison.Ordinal);
        Assert.Contains("oifname \"awg0\" ip daddr 10.0.0.5 udp dport 53 accept", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AClientThatLeavesTheChoiceToTheEndpointTakesWhatItLetsIn()
    {
        var open = EndpointRuleset.Text(
            [Endpoint() with { Id = 7, Inbound = ClientInbound.Network }],
            [Client() with { Address = ["10.0.0.5/32"], Inbound = ClientInbound.Endpoint }],
            "ens3");
        var closed = EndpointRuleset.Text(
            [Endpoint() with { Id = 7, Inbound = ClientInbound.Off }],
            [Client() with { Address = ["10.0.0.5/32"], Inbound = ClientInbound.Endpoint }],
            "ens3");

        Assert.Contains("elements = { 10.0.0.5/32 }", open, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.5", closed, StringComparison.Ordinal);
    }

    [Fact]
    public void AClientOfItsOwnOutweighsTheEndpoint()
    {
        var text = EndpointRuleset.Text(
            [Endpoint() with { Id = 7, Inbound = ClientInbound.Network }],
            [
                Client() with { Address = ["10.0.0.5/32"], Inbound = ClientInbound.Off },
                Client() with { Address = ["10.0.0.6/32"], Inbound = ClientInbound.Network },
            ],
            "ens3");

        Assert.Contains("elements = { 10.0.0.6/32 }", text, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.5", text, StringComparison.Ordinal);
    }

    private const string Key = "6JqDBRc3ZDx6bTcJKKZS0J1yZbSbUKmzS1eJdWQFRVo=";

    private static TunnelClient Client() => new()
    {
        ConfigId = 7,
        Name = "one",
        PublicKey = "u1u1BRc3ZDx6bTcJKKZS0J1yZbSbUKmzS1eJdWQFRVo=",
        IsEnabled = true,
    };

    private static ServerConfig Endpoint() => new()
    {
        Name = "awg0",
        Host = "myvpn-ru.ddns.net",
        ListenPort = 51820,
        Address = ["10.0.0.1/24", "fd42:6d79:7670::cafe:1/112"],
        Dns = ["1.1.1.1"],
        AllowedIps = ["0.0.0.0/0", "::/0"],
        Mtu = 1360,
        Keepalive = 25,
        IsEnabled = true,
        Nat = true,
        Blocked = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"],
        PrivateKey = Key,
        Obfuscation = new ObfuscationSettings
        {
            Jc = 6,
            Jmin = 52,
            Jmax = 241,
            S1 = 63,
            S2 = 149,
            H1 = "194488238-194553774",
            H2 = "945380663",
            H3 = "1220926369",
            H4 = "2008138652",
            HeaderProtectionKey = "wHS9TkIlE61wUO3AglOpnvrCIWDQ5vmoa20cbOEW56k=",
        },
    };
}
