using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using Microsoft.AspNetCore.WebUtilities;

namespace AmneziaGeo.Server.Tests;

public sealed class PlainWireGuardTests
{
    [Fact]
    public void ThePlainPresetPassesTheSameValidationAsAnApiRequest()
    {
        var config = ConfigDefaults.Fresh("plain") with { Obfuscation = ConfigDefaults.Plain() };

        Assert.True(ConfigRules.IsPlain(config.Obfuscation));
        Assert.Null(ConfigRules.Check(config));
        Assert.Null(ConfigRules.CheckObfuscation(config.Obfuscation));
        Assert.False(ConfigRules.IsPlain(ConfigDefaults.Fresh("normal").Obfuscation));
    }

    public static TheoryData<ObfuscationSettings> NonPlainShapes => new(
        new ObfuscationSettings { Jc = 1 }, new ObfuscationSettings { Jmin = 1, Jmax = 1 },
        new ObfuscationSettings { Jmax = 1 }, new ObfuscationSettings { S1 = 1 },
        new ObfuscationSettings { S2 = 1 }, new ObfuscationSettings { S3 = 1 }, new ObfuscationSettings { S4 = 1 },
        new ObfuscationSettings { H1 = "5" }, new ObfuscationSettings { H2 = "6" },
        new ObfuscationSettings { H3 = "7" }, new ObfuscationSettings { H4 = "8" },
        new ObfuscationSettings { I1 = "<b 1>" }, new ObfuscationSettings { I2 = "<b 1>" },
        new ObfuscationSettings { I3 = "<b 1>" }, new ObfuscationSettings { I4 = "<b 1>" }, new ObfuscationSettings { I5 = "<b 1>" },
        new ObfuscationSettings { HeaderProtectionKey = "key" }, new ObfuscationSettings { ContentPaddingAddition = "1" },
        new ObfuscationSettings { RekeyAfterTime = "1" }, new ObfuscationSettings { RekeyTimeout = "1" },
        new ObfuscationSettings { RejectAfterTime = "1" }, new ObfuscationSettings { KeepaliveTimeout = "1" },
        new ObfuscationSettings { MaxHandshakeAttempts = "1" }, new ObfuscationSettings { RandomTrailers = true },
        new ObfuscationSettings { DisableCookies = true });

    [Theory]
    [MemberData(nameof(NonPlainShapes))]
    public void NoAwgParameterCanHideBehindEmptyPacketTypes(ObfuscationSettings shape)
    {
        Assert.False(ConfigRules.IsPlain(shape));
        Assert.NotNull(ConfigRules.CheckObfuscation(shape));
    }

    [Fact]
    public void NonstandardPacketTypesRemainObfuscatedEvenWithNoJunk()
    {
        var shape = new ObfuscationSettings { H1 = "5", H2 = "6", H3 = "7", H4 = "8" };
        var endpoint = Endpoint() with { Obfuscation = shape };

        Assert.Null(ConfigRules.CheckObfuscation(shape));
        Assert.False(ConfigRules.IsPlain(shape));
        Assert.Empty(ClientWireLink.Link(endpoint, Client()));
    }

    [Fact]
    public void PlainTextAndVpnLinksKeepOrdinaryPresharedKeys()
    {
        var endpoint = Endpoint();
        var client = Client() with { PresharedKey = Curve25519.Create().PrivateKey };
        var text = ClientText.Text(endpoint, client);

        Assert.Contains("PresharedKey = " + client.PresharedKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Jc =", text, StringComparison.Ordinal);
        Assert.DoesNotContain("H1 =", text, StringComparison.Ordinal);
        Assert.StartsWith("vpn://", ClientLink.Link(endpoint, client));
        Assert.Empty(ClientWireLink.Link(endpoint, client));
    }

    [Fact]
    public void AnInheritedPresharedKeyAlsoPreventsAnUnusableLink()
    {
        var endpoint = Endpoint() with { PresharedKey = Curve25519.Create().PrivateKey };

        Assert.Empty(ClientWireLink.Link(endpoint, Client()));
    }

    [Theory]
    [InlineData("vpn.example", "vpn.example")]
    [InlineData("2001:db8::1", "[2001:db8::1]")]
    public void LinksEscapeKeysAddressesAndTitlesAndUseTheTemplateMtu(string host, string endpointHost)
    {
        var endpoint = Endpoint() with { Host = host };
        var client = Client() with { Address = ["10.77.171.2/32", "fd77:171::2/128"] };
        var link = ClientWireLink.Link(endpoint, client, TemplateDefaults.Fresh() with { Mtu = 1280 }, "Телефон #1 & +");
        var uri = new Uri(link);
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.StartsWith("wireguard://", link);
        Assert.Equal(client.PrivateKey, Uri.UnescapeDataString(uri.UserInfo));
        Assert.Contains("@" + endpointHost + ":51971?publickey=", link, StringComparison.Ordinal);
        Assert.Equal(endpoint.PublicKey, query["publickey"].ToString());
        Assert.Equal("10.77.171.2/32,fd77:171::2/128", query["address"].ToString());
        Assert.Equal("1280", query["mtu"].ToString());
        Assert.Equal("0,0,0", query["reserved"].ToString());
        Assert.Equal("Телефон #1 & +", Uri.UnescapeDataString(uri.Fragment[1..]));
        Assert.DoesNotContain("preshared", link, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allowinsecure", link, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AZeroMtuIsOmittedAndMissingConnectionDataPreventsLinks()
    {
        var endpoint = Endpoint() with { Mtu = 0 };
        Assert.DoesNotContain("mtu=", ClientWireLink.Link(endpoint, Client()), StringComparison.Ordinal);
        Assert.Empty(ClientWireLink.Link(endpoint with { Host = "" }, Client()));
        Assert.Empty(ClientWireLink.Link(endpoint with { PublicKey = "" }, Client()));
        Assert.Empty(ClientWireLink.Link(endpoint, Client() with { PrivateKey = "" }));
        Assert.Empty(ClientWireLink.Link(endpoint, Client() with { Address = [] }));
    }

    [Fact]
    public void OlderResponseConstructorsKeepTheirFields()
    {
        var response = new ClientConfigResponse("name.conf", "text", "vpn://", "https://subscription", "");

        Assert.Equal("https://subscription", response.Subscription);
        Assert.Empty(response.Wireguard);
    }

    private static ServerConfig Endpoint() => ConfigDefaults.Fresh("plain") with
    {
        Host = "vpn.example", ListenPort = 51971, Address = ["10.77.171.1/24"], Obfuscation = ConfigDefaults.Plain(),
    };

    private static TunnelClient Client()
    {
        var pair = Curve25519.Create();

        return new TunnelClient { Name = "fixture", PrivateKey = pair.PrivateKey, PublicKey = pair.PublicKey, Address = ["10.77.171.2/32"] };
    }
}
