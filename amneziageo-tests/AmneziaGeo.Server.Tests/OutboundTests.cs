using AmneziaGeo.Server.Api.Outbounds;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Tests;

public class OutboundTests
{
    private const string Sample = """
        [Interface]
        PrivateKey = OMMbTfBGZmzVOenkLmL2hFRuMbAG9pOZzTZ4L1H+wF0=
        Address = 10.8.1.5/32, fd00::5/128
        DNS = 1.1.1.1, 8.8.8.8
        MTU = 1380
        Jc = 4
        Jmin = 50
        Jmax = 1000
        S1 = 76
        S2 = 45
        H1 = 1234567
        H2 = 2234567
        H3 = 3234567
        H4 = 4234567

        [Peer]
        PublicKey = eOWG0GXBLrpZOZ+FCwCJTvZ5rBEKHZ8NPvxNM3nJPl4=
        PresharedKey = mDrTJlNmwLJvJHm1TQKHKKFAF0eRDhOOP4nHDXBVOgo=
        AllowedIPs = 0.0.0.0/0, ::/0
        Endpoint = bor.sytes.net:51821
        PersistentKeepalive = 25
        """;

    [Fact]
    public void ATunnelIsReadOutOfAClientConfiguration()
    {
        var read = OutboundImport.Read(Sample, "awgbor");

        Assert.Null(read.Fault);
        var outbound = read.Outbound!;
        Assert.Equal("awgbor", outbound.Name);
        Assert.Equal(OutboundKind.Wg, outbound.Kind);
        Assert.Equal("bor.sytes.net", outbound.Host);
        Assert.Equal(51821, outbound.Port);
        Assert.Equal(["10.8.1.5/32", "fd00::5/128"], outbound.Address);
        Assert.Equal(["1.1.1.1", "8.8.8.8"], outbound.Dns);
        Assert.Equal(1380, outbound.Mtu);
        Assert.Equal(25, outbound.Keepalive);
        Assert.Equal(4, outbound.Obfuscation.Jc);
        Assert.Equal(76, outbound.Obfuscation.S1);
        Assert.Equal("4234567", outbound.Obfuscation.H4);
        Assert.Equal(Curve25519.PublicOf(outbound.PrivateKey), outbound.PublicKey);
        Assert.NotEmpty(outbound.PresharedKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[Interface]\nAddress = 10.8.1.5/32")]
    [InlineData("[Interface]\nPrivateKey = OMMbTfBGZmzVOenkLmL2hFRuMbAG9pOZzTZ4L1H+wF0=")]
    public void AConfigurationWithoutAKeyOrAServerIsRefused(string text)
    {
        var read = OutboundImport.Read(text, "awgbor");

        Assert.Null(read.Outbound);
        Assert.Equal("bad-import", read.Fault!.Code);
    }

    [Fact]
    public void AnEndpointWrittenAsAnAddressWithBracketsIsRead()
    {
        var text = Sample.Replace("bor.sytes.net:51821", "[fd00::1]:51821", StringComparison.Ordinal);

        var read = OutboundImport.Read(text, "awgbor");

        Assert.Equal("fd00::1", read.Outbound!.Host);
        Assert.Equal(51821, read.Outbound.Port);
    }

    [Fact]
    public void ATunnelTurnsIntoOnePeerCarryingEverything()
    {
        var outbound = Tunnel() with { Mark = OutboundRules.FirstMark };

        var update = OutboundDevice.Update(outbound, new System.Net.IPEndPoint(System.Net.IPAddress.Parse("46.8.237.222"), outbound.Port));

        Assert.Equal(outbound.Name, update.Name);
        Assert.Equal(outbound.PrivateKey, update.PrivateKey);
        Assert.Null(update.Fwmark);
        Assert.True(update.ReplacePeers);
        var peer = Assert.Single(update.Peers);
        Assert.Equal(outbound.PeerKey, peer.PublicKey);
        Assert.Equal("46.8.237.222:51821", peer.Endpoint!.ToString());
        Assert.Equal(25u, peer.PersistentKeepalive!.Value.Low);
        Assert.True(peer.ReplaceAllowedIps);
        Assert.Equal(["0.0.0.0/0", "::/0"], peer.AllowedIps.Select(range => range.ToString()));
    }

    [Fact]
    public void AMarkCarriesItsOwnTableAndItsPlaceInTheRuleList()
    {
        Assert.Equal(OutboundRules.FirstTable, OutboundRules.TableOf(OutboundRules.FirstMark, OutboundKind.Wg));
        Assert.Equal(OutboundRules.FirstTable + 3, OutboundRules.TableOf(OutboundRules.FirstMark + 3, OutboundKind.Wg));
        Assert.Equal(OutboundRules.MainTable, OutboundRules.TableOf(OutboundRules.FirstMark + 3, OutboundKind.Local));
        Assert.Equal(OutboundRules.FirstPriority, OutboundRules.PriorityOf(OutboundRules.FirstMark));
    }

    [Fact]
    public void ATunnelWithEverythingInPlaceHolds()
    {
        Assert.Null(OutboundRules.Check(Tunnel()));
        Assert.Null(OutboundRules.Check(OutboundDefaults.Direct()));
    }

    [Theory]
    [InlineData("Name", "bad-interface-name")]
    [InlineData("Kind", "bad-kind")]
    [InlineData("Host", "bad-host")]
    [InlineData("Port", "bad-port")]
    [InlineData("PrivateKey", "bad-key")]
    [InlineData("PeerKey", "bad-peer-key")]
    [InlineData("PresharedKey", "bad-preshared")]
    [InlineData("Address", "bad-address")]
    [InlineData("Dns", "bad-dns")]
    [InlineData("Mtu", "bad-mtu")]
    [InlineData("Keepalive", "bad-keepalive")]
    [InlineData("Mark", "bad-mark")]
    public void ASettingOutOfShapeNamesItself(string setting, string code)
    {
        var outbound = Break(Tunnel(), setting);

        Assert.Equal(code, OutboundRules.Check(outbound)!.Code);
    }

    [Fact]
    public void AnOutboundThroughTheHostItselfCarriesNoServer()
    {
        var outbound = OutboundDefaults.Direct() with { Host = "bor.sytes.net" };

        Assert.Equal("bad-kind", OutboundRules.Check(outbound)!.Code);
    }

    [Fact]
    public void ATableThatDoesNotGoWithTheMarkIsRefused()
    {
        var outbound = Tunnel() with { Table = OutboundRules.FirstTable + 7 };

        Assert.Equal("bad-table", OutboundRules.Check(outbound)!.Code);
    }

    [Fact]
    public void AHandshakeOlderThanTheLimitLeavesTheTunnelDead()
    {
        var now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

        Assert.True(OutboundDevice.Alive(now.AddMinutes(-1), now));
        Assert.False(OutboundDevice.Alive(now.AddMinutes(-5), now));
        Assert.False(OutboundDevice.Alive(null, now));
    }

    [Fact]
    public void TheRulesetMasqueradesAndClampsEveryWayOut()
    {
        var outbounds = (OutboundConfig[])[OutboundDefaults.Direct(), Tunnel()];

        var text = OutboundRuleset.Text(outbounds, "eth0");

        Assert.Contains("table inet " + OutboundRuleset.TableName, text, StringComparison.Ordinal);
        Assert.Contains("delete table inet " + OutboundRuleset.TableName, text, StringComparison.Ordinal);
        Assert.Contains("oifname \"eth0\" masquerade", text, StringComparison.Ordinal);
        Assert.Contains("oifname \"awgbor\" masquerade", text, StringComparison.Ordinal);
        Assert.Contains("oifname \"awgbor\" " + OutboundRuleset.Clamp, text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRulesetLeavesOutWhatIsTurnedOff()
    {
        var outbounds = (OutboundConfig[])[OutboundDefaults.Direct(), Tunnel() with { IsEnabled = false }];

        Assert.Equal(["eth0"], OutboundRuleset.Links(outbounds, "eth0"));
    }

    [Fact]
    public void TwoOutboundsThroughTheHostItselfNameTheUplinkOnce()
    {
        var outbounds = (OutboundConfig[])
        [
            OutboundDefaults.Direct(),
            OutboundDefaults.Direct() with { Name = "second" },
        ];

        Assert.Equal(["eth0"], OutboundRuleset.Links(outbounds, "eth0"));
    }

    [Fact]
    public void ATunnelToAPlainWireGuardServerCarriesNoObfuscation()
    {
        var outbound = Tunnel() with { Obfuscation = new ObfuscationSettings() };

        Assert.Null(OutboundRules.Check(outbound));
        Assert.Null(OutboundDevice.Update(outbound, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, outbound.Port)).Obfuscation);
    }

    [Fact]
    public void ATunnelLetsBackOnlyWhatAnswersTheHost()
    {
        var text = OutboundRuleset.Text([OutboundDefaults.Direct(), Tunnel()], "eth0");

        Assert.Equal(2, Times(text, "iifname { \"awgbor\" } ct state established,related accept"));
        Assert.Equal(2, Times(text, "iifname { \"awgbor\" } drop"));
        Assert.True(
            text.IndexOf("iifname { \"awgbor\" } ct state", StringComparison.Ordinal)
            < text.IndexOf("iifname { \"awgbor\" } drop", StringComparison.Ordinal));
        Assert.DoesNotContain("iifname { \"eth0\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingGoesBackOutTheUplinkItCameIn()
    {
        var text = OutboundRuleset.Text([OutboundDefaults.Direct(), Tunnel() with { IsEnabled = false }], "eth0");

        Assert.Contains("iifname \"eth0\" oifname \"eth0\" drop", text, StringComparison.Ordinal);
        Assert.DoesNotContain("iifname {", text, StringComparison.Ordinal);
        Assert.DoesNotContain("reject", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATunnelClosesThePrivateNetworksBehindItUnlessToldOtherwise()
    {
        var closed = OutboundRuleset.Text([Tunnel()], "eth0");
        var open = OutboundRuleset.Text([Tunnel() with { ClosePrivate = false }], "eth0");

        Assert.Contains(
            "oifname \"awgbor\" ct state new ip daddr { 10.0.0.0/8, 100.64.0.0/10, 169.254.0.0/16, 172.16.0.0/12, 192.168.0.0/16 } reject",
            closed,
            StringComparison.Ordinal);
        Assert.Contains("oifname \"awgbor\" ct state new ip6 daddr { fc00::/7, fe80::/10 } reject", closed, StringComparison.Ordinal);
        Assert.DoesNotContain("reject", open, StringComparison.Ordinal);
        Assert.Contains("iifname { \"awgbor\" } drop", open, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewTunnelClosesThePrivateNetworks()
    {
        Assert.True(OutboundDefaults.Fresh("awgbor").ClosePrivate);
        Assert.True(OutboundImport.Read(Sample, "awgbor").Outbound!.ClosePrivate);
    }

    [Fact]
    public void TheInterfaceTakesEachAddressAlone()
    {
        Assert.Equal(["10.9.0.2/32", "fd00::2/128"], OutboundDevice.Hosts(["10.9.0.2/24", "fd00::2/64"]));
        Assert.Equal(["10.8.1.5/32"], OutboundDevice.Hosts(["10.8.1.5/32"]));
    }

    [Fact]
    public void AnOutboundSentWithoutTheFlagClosesThePrivateNetworks()
    {
        Assert.True(OutboundAnswers.Draft(Asked(null)).ClosePrivate);
        Assert.False(OutboundAnswers.Draft(Asked(false)).ClosePrivate);
    }

    private static OutboundRequest Asked(bool? closePrivate) => new(
        Name: "awgbor",
        Kind: OutboundKind.Wg,
        IsEnabled: true,
        Host: "bor.sytes.net",
        Port: 51821,
        Proxy: null,
        PrivateKey: null,
        PeerKey: null,
        PresharedKey: null,
        Address: ["10.8.1.5/32"],
        Dns: [],
        Mtu: 1380,
        Keepalive: 25,
        Probe: null,
        ProbeEvery: 30,
        ClosePrivate: closePrivate,
        Obfuscation: null);

    private static int Times(string text, string part) => text.Split(part).Length - 1;

    private static OutboundConfig Tunnel() => new()
    {
        Name = "awgbor",
        Kind = OutboundKind.Wg,
        IsEnabled = true,
        Host = "bor.sytes.net",
        Port = 51821,
        PrivateKey = "OMMbTfBGZmzVOenkLmL2hFRuMbAG9pOZzTZ4L1H+wF0=",
        PeerKey = "eOWG0GXBLrpZOZ+FCwCJTvZ5rBEKHZ8NPvxNM3nJPl4=",
        Address = ["10.8.1.5/32"],
        Dns = ["1.1.1.1"],
        Mtu = 1380,
        Keepalive = 25,
        Mark = OutboundRules.FirstMark,
        Table = OutboundRules.FirstTable,
        Obfuscation = new ObfuscationSettings { Jc = 4, Jmin = 50, Jmax = 1000, S1 = 76, S2 = 45, H1 = "5", H2 = "6", H3 = "7", H4 = "8" },
    };

    private static OutboundConfig Break(OutboundConfig outbound, string setting) => setting switch
    {
        "Name" => outbound with { Name = "Awg Bor" },
        "Kind" => outbound with { Kind = "socks" },
        "Host" => outbound with { Host = "not a host" },
        "Port" => outbound with { Port = 0 },
        "PrivateKey" => outbound with { PrivateKey = "short" },
        "PeerKey" => outbound with { PeerKey = "short" },
        "PresharedKey" => outbound with { PresharedKey = "short" },
        "Address" => outbound with { Address = ["10.8.1.5/64"] },
        "Dns" => outbound with { Dns = ["nameserver"] },
        "Mtu" => outbound with { Mtu = 60 },
        "Keepalive" => outbound with { Keepalive = -1 },
        _ => outbound with { Mark = 1, Table = OutboundRules.TableOf(1, OutboundKind.Wg) },
    };
}
