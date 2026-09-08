using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public class ClientTests
{
    [Fact]
    public void AFreshClientCarriesAKeyPairOfItsOwn()
    {
        var client = ClientDefaults.Fresh(1, "milena");

        Assert.True(Curve25519.IsKey(client.PrivateKey));
        Assert.Equal(Curve25519.PublicOf(client.PrivateKey), client.PublicKey);
    }

    [Fact]
    public void AFreeAddressComesOutOfEveryRangeOfTheEndpoint()
    {
        var picked = ClientPool.Free(["10.8.0.1/24", "fd00:dead:beef::cafe:1/112"], ["10.8.0.1/24"]);

        Assert.Equal(["10.8.0.2/32", "fd00:dead:beef::cafe:2/128"], picked);
    }

    [Fact]
    public void AnAddressAnotherClientCarriesIsPassedOver()
    {
        var picked = ClientPool.Free(["10.8.0.1/24"], ["10.8.0.1/24", "10.8.0.2/32", "10.8.0.3/32"]);

        Assert.Equal(["10.8.0.4/32"], picked);
    }

    [Fact]
    public void ARangeWithNoRoomLeftHandsOutNothing()
    {
        var picked = ClientPool.Free(["10.8.0.0/30"], ["10.8.0.2/32", "10.8.0.3/32"]);

        Assert.Empty(picked);
    }

    [Fact]
    public void TheConfigurationOfAClientCarriesTheObfuscationOfTheEndpoint()
    {
        var text = ClientText.Text(Endpoint(), Client());

        Assert.Contains("Jc = 5", text, StringComparison.Ordinal);
        Assert.Contains("H1 = 263252104", text, StringComparison.Ordinal);
        Assert.Contains("H2 = 194488238-194553774", text, StringComparison.Ordinal);
        Assert.Contains("HeaderProtectionKey = wHS9TkIlE61wUO3AglOpnvrCIWDQ5vmoa20cbOEW56k=", text, StringComparison.Ordinal);
        Assert.Contains("ContentPaddingAddition = 12-44", text, StringComparison.Ordinal);
        Assert.Contains("RekeyAfterTime = 100-135", text, StringComparison.Ordinal);
        Assert.Contains("MaxHandshakeAttempts = 17-33", text, StringComparison.Ordinal);
        Assert.Contains("Endpoint = bor.sytes.net:51820", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 0.0.0.0/0, ::/0", text, StringComparison.Ordinal);
        Assert.Contains("PersistentKeepalive = 25", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AClientWithoutAKeyOfItsOwnTakesTheOneOfTheEndpoint()
    {
        var endpoint = Endpoint() with { PresharedKey = "Odf24IgTX26F2hpdemxeQffynDs6zAsdKcLMOW1lrl8=" };

        Assert.Contains("PresharedKey = Odf24IgTX26F2hpdemxeQffynDs6zAsdKcLMOW1lrl8=", ClientText.Text(endpoint, Client()), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEndpointWithoutAnAddressLeavesTheLineOut()
    {
        var text = ClientText.Text(Endpoint() with { Host = string.Empty }, Client());

        Assert.DoesNotContain("Endpoint =", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePeerOfAClientCarriesTheAddressesOfTheClient()
    {
        var peer = ClientDevice.Peer(Endpoint(), Client());

        Assert.True(peer.ReplaceAllowedIps);
        Assert.Equal("10.8.0.2/32", peer.AllowedIps[0].ToString());
    }

    [Fact]
    public void OnlyTheClientsThatAreOnGoOnTheInterface()
    {
        var update = ClientDevice.Update(Endpoint(), [Client(), Client("off") with { IsEnabled = false }]);

        Assert.Single(update.Peers);
        Assert.Equal("awg1", update.Name);
    }

    [Fact]
    public void ThePeersThePanelDoesNotHoldAreNamed()
    {
        var client = Client();

        var stale = ClientDevice.Stale([client.PublicKey, "PjJKnA1FOVjJl9Ub1VvoJ8OKXFgLPFcwJjzLU+AWSlE="], [client]);

        Assert.Equal(["PjJKnA1FOVjJl9Ub1VvoJ8OKXFgLPFcwJjzLU+AWSlE="], stale);
    }

    [Fact]
    public void TheInterfaceFileKeepsTheSettingsWrittenAboveTheClients()
    {
        var held = "[Interface]\nPrivateKey = k\nListenPort = 443\n\n# old\n[Peer]\nPublicKey = p\n";

        Assert.Equal("[Interface]\nPrivateKey = k\nListenPort = 443", InterfaceFile.Head(held));
    }

    [Fact]
    public void TheInterfaceFileWritesEveryClientThatIsOn()
    {
        var text = InterfaceFile.Text("[Interface]", Endpoint(), [Client(), Client("off") with { IsEnabled = false }]);

        Assert.Contains("# milena", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 10.8.0.2/32", text, StringComparison.Ordinal);
        Assert.DoesNotContain("# off", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePeersOfAnInterfaceFileBecomeClients()
    {
        var file = """
            [Interface]
            PrivateKey = kEnCIRhKjNllB2d7J7t5gMAySOXbC9KKrcPwIbkgGUc=

            # home tv
            [Peer]
            PublicKey = eyf4cmoVSwDUFfS+15aWOaz3jYFQ1pPraV/WkHu3zDk=
            PresharedKey = Odf24IgTX26F2hpdemxeQffynDs6zAsdKcLMOW1lrl8=
            AllowedIPs = 10.8.2.10/32, fdcc::cafe:a/128
            """;

        var clients = ClientImport.Peers(file, 7);

        Assert.Single(clients);
        Assert.Equal("home-tv", clients[0].Name);
        Assert.Equal(7, clients[0].ConfigId);
        Assert.Equal(["10.8.2.10/32", "fdcc::cafe:a/128"], clients[0].Address);
        Assert.Equal("Odf24IgTX26F2hpdemxeQffynDs6zAsdKcLMOW1lrl8=", clients[0].PresharedKey);
    }

    [Fact]
    public void APeerWithoutANameAboveItIsNumbered()
    {
        Assert.Equal("peer-3", ClientImport.Name(string.Empty, 3));
    }

    [Fact]
    public void AnInterfaceFileBecomesAnEndpoint()
    {
        var file = """
            [Interface]
            PrivateKey = kEnCIRhKjNllB2d7J7t5gMAySOXbC9KKrcPwIbkgGUc=
            Address = 10.0.1.1/24, fdcc::cafe:1/112
            ListenPort = 443
            MTU = 1340
            Jc = 6
            H1 = 194488238-194553774
            I1 = <r 246>
            HeaderProtectionKey = wHS9TkIlE61wUO3AglOpnvrCIWDQ5vmoa20cbOEW56k=
            ContentPaddingAddition = 12-44
            RekeyAfterTime = 100-135
            RekeyTimeout = 5-6
            RejectAfterTime = 186-259
            KeepaliveTimeout = 12-16
            MaxHandshakeAttempts = 17-33
            RandomTrailers = on
            DisableCookies = on
            """;

        var read = ConfigImport.Read(file, "awg1");

        Assert.Null(read.Fault);
        Assert.Equal(443, read.Config!.ListenPort);
        Assert.Equal(1340, read.Config.Mtu);
        Assert.Equal(["10.0.1.1/24", "fdcc::cafe:1/112"], read.Config.Address);
        Assert.Equal("194488238-194553774", read.Config.Obfuscation.H1);
        Assert.Equal("<r 246>", read.Config.Obfuscation.I1);
        Assert.Equal("wHS9TkIlE61wUO3AglOpnvrCIWDQ5vmoa20cbOEW56k=", read.Config.Obfuscation.HeaderProtectionKey);
        Assert.Equal("12-44", read.Config.Obfuscation.ContentPaddingAddition);
        Assert.Equal("5-6", read.Config.Obfuscation.RekeyTimeout);
        Assert.Equal("186-259", read.Config.Obfuscation.RejectAfterTime);
        Assert.Equal("12-16", read.Config.Obfuscation.KeepaliveTimeout);
        Assert.Equal("17-33", read.Config.Obfuscation.MaxHandshakeAttempts);
        Assert.True(read.Config.Obfuscation.RandomTrailers);
        Assert.True(read.Config.Obfuscation.DisableCookies);
    }

    [Fact]
    public void AnInterfaceFileWithoutAPrivateKeyIsRefused()
    {
        var read = ConfigImport.Read("[Interface]\nListenPort = 443\n", "awg1");

        Assert.Null(read.Config);
        Assert.Equal("bad-import", read.Fault!.Code);
    }

    [Fact]
    public void AClientCountsAsConnectedWhileItsHandshakeIsFresh()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var client = Client();
        var peer = new AwgPeer { PublicKey = client.PublicKey, LastHandshake = now.AddMinutes(-1) };

        var state = ClientState.Of(client, peer, now);

        Assert.True(state.IsOnline);
        Assert.True(state.IsPresent);
    }

    [Fact]
    public void AClientTheInterfaceForgotIsNeitherOnlineNorPresent()
    {
        var state = ClientState.Of(Client(), null, DateTimeOffset.UnixEpoch);

        Assert.False(state.IsOnline);
        Assert.False(state.IsPresent);
    }

    [Fact]
    public void ANameLongerThanTheRuleIsRefused()
    {
        Assert.Equal("bad-client-name", ClientRules.CheckName(new string('a', 65))!.Code);
    }

    [Fact]
    public void AClientWithoutAnAddressIsRefused()
    {
        Assert.Equal("bad-client-address", ClientRules.CheckAddress([])!.Code);
    }

    private static ServerConfig Endpoint() => new()
    {
        Id = 1,
        Name = "awg1",
        Host = "bor.sytes.net",
        ListenPort = 51820,
        Address = ["10.8.0.1/24"],
        Dns = ["1.1.1.1"],
        AllowedIps = ["0.0.0.0/0", "::/0"],
        Mtu = 1420,
        Keepalive = 25,
        PrivateKey = "kEnCIRhKjNllB2d7J7t5gMAySOXbC9KKrcPwIbkgGUc=",
        PublicKey = "6isSFA1FOVjJl9Ub1VvoJ8OKXFgLPFcwJjzLU+AWSlE=",
        Obfuscation = new ObfuscationSettings
        {
            Jc = 5,
            Jmin = 50,
            Jmax = 1000,
            S1 = 109,
            S2 = 136,
            H1 = "263252104",
            H2 = "194488238-194553774",
            HeaderProtectionKey = "wHS9TkIlE61wUO3AglOpnvrCIWDQ5vmoa20cbOEW56k=",
            ContentPaddingAddition = "12-44",
            RekeyAfterTime = "100-135",
            MaxHandshakeAttempts = "17-33",
        },
    };

    private static TunnelClient Client(string name = "milena") => new()
    {
        ConfigId = 1,
        Name = name,
        PrivateKey = "qA3cEwnAVQIDQTRPDGmDN6fzrDwARw2mnJ4jswPibnY=",
        PublicKey = "eyf4cmoVSwDUFfS+15aWOaz3jYFQ1pPraV/WkHu3zDk=",
        Address = ["10.8.0.2/32"],
        IsEnabled = true,
    };
}
