using System.Buffers.Binary;
using System.Buffers.Text;
using System.IO.Compression;
using System.Text.Json;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public class ClientTests
{
    [Fact]
    public void AClientWithoutKeysIsGivenAPair()
    {
        var keyed = ClientDefaults.Keyed(new TunnelClient { ConfigId = 1, Name = "milena" });

        Assert.True(Curve25519.IsKey(keyed.PrivateKey));
        Assert.Equal(Curve25519.PublicOf(keyed.PrivateKey), keyed.PublicKey);
    }

    [Fact]
    public void AClientWithOnlyAPrivateKeyIsGivenThePublicOne()
    {
        var pair = Curve25519.Create();
        var keyed = ClientDefaults.Keyed(new TunnelClient { ConfigId = 1, Name = "milena", PrivateKey = pair.PrivateKey });

        Assert.Equal(pair.PrivateKey, keyed.PrivateKey);
        Assert.Equal(pair.PublicKey, keyed.PublicKey);
    }

    [Fact]
    public void AClientThatBringsItsOwnPublicKeyKeepsIt()
    {
        var pair = Curve25519.Create();
        var client = new TunnelClient { ConfigId = 1, Name = "milena", PublicKey = pair.PublicKey };

        Assert.Same(client, ClientDefaults.Keyed(client));
    }

    [Fact]
    public void ANewClientStartsOnTheOnlyEndpoint()
    {
        var only = Endpoint();

        Assert.Same(only, ClientDefaults.Endpoint([only], []));
        Assert.Same(only, ClientDefaults.Endpoint([only], [Client() with { Id = 7 }]));
    }

    [Fact]
    public void ANewClientStartsOnTheEndpointOfTheNewestClient()
    {
        var first = Endpoint();
        var second = Endpoint() with { Id = 2, Name = "awg2" };
        var clients = new[] { Client() with { Id = 3, ConfigId = 2 }, Client("bogdan") with { Id = 9, ConfigId = 1 } };

        Assert.Same(first, ClientDefaults.Endpoint([first, second], clients));
        Assert.Same(second, ClientDefaults.Endpoint([first, second], [clients[0]]));
    }

    [Fact]
    public void ANewClientStartsOnTheFirstEndpointWhenNoClientHoldsOne()
    {
        var first = Endpoint() with { Id = 4, Name = "awg0" };
        var second = Endpoint() with { Id = 2, Name = "awg2" };

        Assert.Same(first, ClientDefaults.Endpoint([first, second], []));
        Assert.Same(first, ClientDefaults.Endpoint([first, second], [Client() with { Id = 5, ConfigId = 8 }]));
        Assert.Null(ClientDefaults.Endpoint([], [Client() with { Id = 5 }]));
    }

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
        var picked = ClientPool.Free(["10.8.0.1/30"], ["10.8.0.2/32"]);

        Assert.Empty(picked);
    }

    [Fact]
    public void TheSameNumberComesOutOfEveryRange()
    {
        var picked = ClientPool.Free(["10.8.0.1/24", "fd00::1/64"], ["10.8.0.2/32", "fd00::3/128"]);

        Assert.Equal(["10.8.0.4/32", "fd00::4/128"], picked);
    }

    [Fact]
    public void AnAddressFitsOnlyInsideTheRangesAndOffTheirEdges()
    {
        Assert.Null(Fitting("10.8.0.5/32", "fd00::5/128"));
        Assert.Null(Fitting("fd00::ff/128"));
        Assert.Equal("client-address-outside", Fitting("10.9.0.5/32"));
        Assert.Equal("client-address-reserved", Fitting("10.8.0.1/32"));
        Assert.Equal("client-address-reserved", Fitting("10.8.0.0/32"));
        Assert.Equal("client-address-reserved", Fitting("10.8.0.255/32"));
        Assert.Equal("client-address-reserved", Fitting("fd00::/128"));
        Assert.Equal("bad-client-address", Fitting("10.8.0.5/24"));
        Assert.Equal("bad-client-address", Fitting("10.8.0.5/32", "10.8.0.6/32"));
    }

    [Fact]
    public void TheConfigurationOfAClientCarriesTheResolverOfThePanel()
    {
        var text = ClientText.Text(Endpoint(), Client(), null, ["10.8.0.1"]);

        Assert.Contains("DNS = 10.8.0.1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATemplateWithNamesOfItsOwnOutweighsTheResolver()
    {
        var template = new ClientTemplate { Name = "own", Dns = ["9.9.9.9"] };
        var text = ClientText.Text(Endpoint(), Client(), template, ["10.8.0.1"]);

        Assert.Contains("DNS = 9.9.9.9", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATemplateWithoutNamesTakesTheResolver()
    {
        var text = ClientText.Text(Endpoint(), Client(), new ClientTemplate { Name = "plain" }, ["10.8.0.1"]);

        Assert.Contains("DNS = 10.8.0.1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAResolverTheNamesOfTheEndpointStand()
    {
        Assert.Contains("DNS = 1.1.1.1", ClientText.Text(Endpoint(), Client()), StringComparison.Ordinal);
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
    public void TheFileAHostKeepsItsClientsInBecomesClients()
    {
        var file = """
            {
              "awg1": {
                "1": {
                  "email": "home tv",
                  "privateKey": "kEnCIRhKjNllB2d7J7t5gMAySOXbC9KKrcPwIbkgGUc=",
                  "publicKey": "eyf4cmoVSwDUFfS+15aWOaz3jYFQ1pPraV/WkHu3zDk=",
                  "preSharedKey": "Odf24IgTX26F2hpdemxeQffynDs6zAsdKcLMOW1lrl8=",
                  "allowedIPs": ["10.8.0.26/32"],
                  "enable": false
                },
                "2": { "privateKey": "x" }
              }
            }
            """;

        var read = ClientImport.Read(file, "awg1", 7, "fdcc::cafe");

        Assert.Null(read.Fault);
        var client = Assert.Single(read.Clients);
        Assert.Equal("home-tv", client.Name);
        Assert.Equal(7, client.ConfigId);
        Assert.Equal("kEnCIRhKjNllB2d7J7t5gMAySOXbC9KKrcPwIbkgGUc=", client.PrivateKey);
        Assert.Equal(["10.8.0.26/32", "fdcc::cafe:1a/128"], client.Address);
        Assert.False(client.IsEnabled);
    }

    [Fact]
    public void AnImportTellsTheKindOfTextAndThePartOfTheEndpoint()
    {
        var peers = """
            [Interface]
            PrivateKey = kEnCIRhKjNllB2d7J7t5gMAySOXbC9KKrcPwIbkgGUc=

            [Peer]
            PublicKey = eyf4cmoVSwDUFfS+15aWOaz3jYFQ1pPraV/WkHu3zDk=
            AllowedIPs = 10.8.2.10/32
            """;
        var only = """{ "awg0": { "a": { "publicKey": "eyf4cmoVSwDUFfS+15aWOaz3jYFQ1pPraV/WkHu3zDk=" } } }""";
        var two = """{ "awg0": { "a": { "publicKey": "eyf4cmoVSwDUFfS+15aWOaz3jYFQ1pPraV/WkHu3zDk=" } }, "awg2": {} }""";

        Assert.Single(ClientImport.Read(peers, "awg1", 7, null).Clients);
        Assert.Single(ClientImport.Read(only, "awg1", 7, null).Clients);
        Assert.Equal("bad-client-import", ClientImport.Read(two, "awg1", 7, null).Fault!.Code);
        Assert.Equal("bad-client-import", ClientImport.Read("{ broken", "awg1", 7, null).Fault!.Code);
        Assert.Equal("bad-client-import", ClientImport.Read("  ", "awg1", 7, null).Fault!.Code);
        Assert.Equal("bad-client-import", ClientImport.Read("[Interface]\nListenPort = 1\n", "awg1", 7, null).Fault!.Code);
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

    [Fact]
    public void TheLinkCarriesTheSameFileAsTheText()
    {
        using var document = Opened(ClientLink.Link(Endpoint(), Client()));
        var awg = document.RootElement.GetProperty("containers")[0].GetProperty("awg");
        using var last = JsonDocument.Parse(awg.GetProperty("last_config").GetString()!);

        Assert.Equal(ClientText.Text(Endpoint(), Client()), last.RootElement.GetProperty("config").GetString());
        Assert.Equal(51820, last.RootElement.GetProperty("port").GetInt32());
    }

    [Fact]
    public void TheLinkNamesTheClientAndTheServer()
    {
        using var document = Opened(ClientLink.Link(Endpoint(), Client()));
        var root = document.RootElement;

        Assert.Equal("bor.sytes.net-awg1-milena", root.GetProperty("description").GetString());
        Assert.Equal("bor.sytes.net", root.GetProperty("hostName").GetString());
        Assert.Equal("amnezia-awg", root.GetProperty("defaultContainer").GetString());
        Assert.Equal("51820", root.GetProperty("containers")[0].GetProperty("awg").GetProperty("port").GetString());
    }

    [Fact]
    public void TheLinkCarriesTheNameItIsGiven()
    {
        using var document = Opened(ClientLink.Link(Endpoint(), Client(), title: "office-milena"));

        Assert.Equal("office-milena", document.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void TheTemplateNamesTheConfigurationWithTheTextAroundTheSubstitutions()
    {
        var client = Client() with { Id = 17, Note = "Милена", CreatedUtc = Added };

        var name = ClientText.Title(Endpoint(), client, "{INTERFACE}-DE-ЕЩЕ константа-{ID}-{DATE}|{NOTE}:{PORT}");

        Assert.Equal("awg1-DE-ЕЩЕ константа-17-2026-09-23|Милена:51820", name);
    }

    [Theory]
    [InlineData("{HOST}-{INTERFACE}-{CLIENT}", "awg1-milena")]
    [InlineData("{INTERFACE}-{NOTE}-{CLIENT}", "awg1-milena")]
    [InlineData("{INTERFACE}_{NOTE}", "awg1")]
    [InlineData("{NOTE} {CLIENT}", "milena")]
    [InlineData("{INTERFACE}--{CLIENT}", "awg1--milena")]
    public void AnEmptySubstitutionTakesTheSeparatorAfterItAlong(string template, string name)
    {
        Assert.Equal(name, ClientText.Title(Endpoint() with { Host = string.Empty }, Client(), template));
    }

    [Fact]
    public void AnEndpointWithoutAHostIsNamedByTheAddressItWasReachedAt()
    {
        var endpoint = Endpoint() with { Host = string.Empty };

        Assert.Equal("panel.example-awg1-milena", ClientText.Title(endpoint, Client(), host: "panel.example"));
        Assert.Equal("bor.sytes.net-awg1-milena", ClientText.Title(Endpoint(), Client(), host: "panel.example"));
    }

    [Fact]
    public void ANameNothingIsLeftOfTakesTheMomentTheClientWasAdded()
    {
        var client = Client() with { CreatedUtc = Added };

        Assert.Equal("2026-09-23-14-05-33-127", ClientText.Title(Endpoint(), client, "{NOTE}-"));
    }

    [Fact]
    public void TheFileNameReplacesWhatAFileDoesNotTake()
    {
        Assert.Equal("bor.sytes.net-awg1-milena.conf", ClientText.FileName(Endpoint(), Client()));
        Assert.Equal("awg1_milena_x_y.conf", ClientText.FileName(Endpoint(), Client(), "{INTERFACE}|{CLIENT}:x/y."));
    }

    [Fact]
    public void ALongListOfRangesTakesLessRoomInTheLink()
    {
        var ranges = Enumerable.Range(0, 1000).Select(n => $"10.{n / 256}.{n % 256}.0/24").ToArray();
        var endpoint = Endpoint() with { AllowedIps = ranges };

        var link = ClientLink.Link(endpoint, Client());

        Assert.StartsWith("vpn://", link, StringComparison.Ordinal);
        Assert.True(link.Length < ClientText.Text(endpoint, Client()).Length / 2);
    }

    [Fact]
    public void ATemplateReplacesTheSettingsItNames()
    {
        var template = new ClientTemplate
        {
            Name = "blocked",
            AllowedIps = ["10.0.0.0/8", "192.168.0.0/16"],
            Dns = ["9.9.9.9"],
            Mtu = 1280,
            Keepalive = 0,
        };

        var text = ClientText.Text(Endpoint(), Client(), template);

        Assert.Contains("AllowedIPs = 10.0.0.0/8, 192.168.0.0/16", text, StringComparison.Ordinal);
        Assert.Contains("DNS = 9.9.9.9", text, StringComparison.Ordinal);
        Assert.Contains("MTU = 1280", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistentKeepalive", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyTemplateTakesTheSettingsOfTheEndpoint()
    {
        var endpoint = Endpoint() with { Dns = ["10.8.0.1"], AllowedIps = ["10.0.0.0/8"], Mtu = 1280, Keepalive = 0 };

        var text = ClientText.Text(endpoint, Client(), new ClientTemplate { Name = "plain" });

        Assert.Contains("DNS = 10.8.0.1\n", text, StringComparison.Ordinal);
        Assert.Contains("MTU = 1280\n", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 10.0.0.0/8\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistentKeepalive", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyTemplateFallsToTheBuiltInValuesWhereTheEndpointHoldsNone()
    {
        var endpoint = Endpoint() with { Dns = [], AllowedIps = [] };

        var text = ClientText.Text(endpoint, Client(), new ClientTemplate { Name = "plain" });

        Assert.Contains("DNS = 1.1.1.1, 1.0.0.1\n", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 0.0.0.0/0\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheValuesOfAnEndpointReachTheFileOfAClientWhoseTemplateLeavesThemEmpty()
    {
        var endpoint = ConfigDefaults.Fresh("t66i") with
        {
            Host = "bor.sytes.net",
            Mtu = 1234,
            Keepalive = 21,
            AllowedIps = ["10.66.9.0/24"],
            Dns = ["9.9.9.9"],
        };

        var text = ClientText.Text(endpoint, Client(), new ClientTemplate { Name = "plain" });

        Assert.Contains("DNS = 9.9.9.9\n", text, StringComparison.Ordinal);
        Assert.Contains("MTU = 1234\n", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 10.66.9.0/24\n", text, StringComparison.Ordinal);
        Assert.Contains("PersistentKeepalive = 21\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AClientWithoutATemplateKeepsTheSettingsOfTheEndpoint()
    {
        var endpoint = Endpoint() with { Dns = ["10.8.0.1"], AllowedIps = ["10.0.0.0/8"], Mtu = 1280, Keepalive = 0 };

        var text = ClientText.Text(endpoint, Client());

        Assert.Contains("DNS = 10.8.0.1\n", text, StringComparison.Ordinal);
        Assert.Contains("MTU = 1280\n", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 10.0.0.0/8\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistentKeepalive", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyTemplateRoutesIpv6OnlyForAClientThatHoldsIt()
    {
        var client = Client() with { Address = ["10.8.0.2/32", "fdcc:ad94:bacf:61a5::2/128"] };

        var text = ClientText.Text(Endpoint() with { AllowedIps = [] }, client, new ClientTemplate { Name = "plain" });

        Assert.Contains("AllowedIPs = 0.0.0.0/0, ::/0\n", text, StringComparison.Ordinal);
        Assert.Equal(["0.0.0.0/0"], TemplateDefaults.AllowedIps(["10.8.0.1/24"]));
        Assert.Equal(["0.0.0.0/0", "::/0"], TemplateDefaults.AllowedIps(["10.8.0.1/24", "fd00::1/64"]));
    }

    [Fact]
    public void TheLinkCarriesTheTemplateToo()
    {
        var template = new ClientTemplate { Name = "blocked", AllowedIps = ["10.0.0.0/8"] };

        using var document = Opened(ClientLink.Link(Endpoint(), Client(), template));
        var awg = document.RootElement.GetProperty("containers")[0].GetProperty("awg");
        using var last = JsonDocument.Parse(awg.GetProperty("last_config").GetString()!);

        Assert.Contains(
            "AllowedIPs = 10.0.0.0/8\n",
            last.RootElement.GetProperty("config").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ATemplateWithABadNameOrValueIsRefused()
    {
        Assert.Equal("bad-template-name", TemplateRules.Check(new ClientTemplate { Name = " " })!.Code);
        Assert.Equal("bad-allowed", TemplateRules.Check(new ClientTemplate { Name = "a", AllowedIps = ["not-a-range"] })!.Code);
        Assert.Equal("bad-dns", TemplateRules.Check(new ClientTemplate { Name = "a", Dns = ["dns.google"] })!.Code);
        Assert.Equal("bad-mtu", TemplateRules.Check(new ClientTemplate { Name = "a", Mtu = 100 })!.Code);
        Assert.Equal("bad-keepalive", TemplateRules.Check(new ClientTemplate { Name = "a", Keepalive = -1 })!.Code);
        Assert.Null(TemplateRules.Check(new ClientTemplate { Name = "a", Mtu = 1280, Keepalive = 0 }));
    }

    private static string? Fitting(params string[] addresses) =>
        ClientPool.Fit(["10.8.0.1/24", "fd00::1/120"], addresses)?.Code;

    private static JsonDocument Opened(string link)
    {
        var packed = Base64Url.DecodeFromChars(link.AsSpan("vpn://".Length));
        using var source = new MemoryStream(packed, 4, packed.Length - 4);
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);

        Assert.Equal(BinaryPrimitives.ReadInt32BigEndian(packed), (int)output.Length);

        return JsonDocument.Parse(output.ToArray());
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

    [Fact]
    public void AForwardIsReadAndWrittenAsProtocolAndPorts()
    {
        Assert.True(PortForward.TryParse("tcp:2222:22", out var found));
        Assert.Equal(new PortForward("tcp", 2222, 22), found);
        Assert.Equal("udp:5353:53", new PortForward("udp", 5353, 53).ToString());
        Assert.False(PortForward.TryParse("sctp:1:1", out _));
        Assert.False(PortForward.TryParse("tcp:0:22", out _));
        Assert.False(PortForward.TryParse("tcp:65536:22", out _));
        Assert.False(PortForward.TryParse("tcp:22", out _));
    }

    [Fact]
    public void AClientWithABadNetworkOrPortIsRefused()
    {
        Assert.Equal(
            "bad-client-routes",
            ClientRules.Check(Client() with { Routes = ["not-a-network"] })!.Code);
        Assert.Equal(
            "bad-client-routes",
            ClientRules.Check(Client() with { Routes = ["192.168.88.7/24"] })!.Code);
        Assert.Equal(
            "bad-client-forward",
            ClientRules.Check(Client() with { Forwards = [new PortForward("tcp", 0, 22)] })!.Code);
        Assert.Equal(
            "bad-client-forward",
            ClientRules.Check(Client() with
            {
                Forwards = [new PortForward("tcp", 2222, 22), new PortForward("tcp", 2222, 23)],
            })!.Code);
        Assert.Equal(
            "bad-client-inbound",
            ClientRules.Check(Client() with { Inbound = (ClientInbound)7 })!.Code);
        Assert.Null(ClientRules.Check(Client() with
        {
            Routes = ["192.168.88.0/24"],
            Forwards = [new PortForward("tcp", 2222, 22), new PortForward("udp", 2222, 22)],
            Inbound = ClientInbound.Network,
        }));
    }

    [Fact]
    public void TheNetworksBehindAClientGoIntoItsPeer()
    {
        var peer = ClientDevice.Peer(Endpoint(), Client() with { Routes = ["192.168.88.0/24"] });

        Assert.Equal(["10.8.0.2/32", "192.168.88.0/24"], peer.AllowedIps!.Select(range => range.ToString()));
    }

    [Fact]
    public void TheFileOfAClientCarriesNoLineOfTheServerButTheMovedServices()
    {
        var client = Client() with { Inbound = ClientInbound.Network, Routes = ["192.168.88.0/24"], Routing = ClientRouting.Off };

        var plain = ClientText.Text(Endpoint(), client, new ClientTemplate { Name = "closed", Routing = false });
        var moved = ClientText.Text(Endpoint() with { ServicesPort = 8446 }, Client());
        var same = ClientText.Text(Endpoint() with { ServicesPort = Endpoint().ListenPort }, Client());

        Assert.DoesNotContain("# AmneziaGeo", plain, StringComparison.Ordinal);
        Assert.Contains("# AmneziaGeo Services = 8446\n", moved, StringComparison.Ordinal);
        Assert.True(moved.IndexOf("# AmneziaGeo Services", StringComparison.Ordinal) < moved.IndexOf("[Peer]", StringComparison.Ordinal));
        Assert.DoesNotContain("# AmneziaGeo", same, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLinkCarriesTheFileWithoutExtras()
    {
        var client = Client() with { Inbound = ClientInbound.Server, Routes = ["192.168.88.0/24"] };

        using var document = Opened(ClientLink.Link(Endpoint() with { WebSocket = true }, client));

        Assert.False(document.RootElement.TryGetProperty("amneziageo", out _));
    }

    [Fact]
    public void TheRoutingOfAClientIsReadByName()
    {
        Assert.Equal(ClientRouting.Off, RoutingName.Read(" OFF ", ClientRouting.Template));
        Assert.Equal(ClientRouting.On, RoutingName.Read("on", ClientRouting.Template));
        Assert.Equal(ClientRouting.Template, RoutingName.Read("template", ClientRouting.Off));
        Assert.Equal(ClientRouting.Off, RoutingName.Read(null, ClientRouting.Off));
        Assert.Equal((ClientRouting)(-1), RoutingName.Read("maybe", ClientRouting.Template));
        Assert.Equal("template", RoutingName.Of(ClientRouting.Template));
        Assert.Equal("off", RoutingName.Of(ClientRouting.Off));
        Assert.Equal("bad-client-routing", ClientRules.Check(Client() with { Routing = (ClientRouting)7 })!.Code);
        Assert.Null(ClientRules.Check(Client() with { Routing = ClientRouting.Off }));
    }

    [Fact]
    public void AnEndpointTakesNoAccessOfItsOwnBeyondTheThree()
    {
        Assert.Equal("bad-inbound", ConfigRules.CheckInbound(ClientInbound.Endpoint)!.Code);
        Assert.Null(ConfigRules.CheckInbound(ClientInbound.Network));
        Assert.Null(ConfigRules.CheckInbound(ClientInbound.Off));
    }

    private static readonly DateTimeOffset Added = new(2026, 9, 23, 14, 5, 33, 127, TimeSpan.Zero);

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
