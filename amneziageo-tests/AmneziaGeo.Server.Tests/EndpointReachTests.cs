using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Tests;

public class EndpointReachTests
{
    private const string Root = "/etc/letsencrypt/live";

    [Fact]
    public void AnEndpointThatIsOnNeedsAHost()
    {
        var bare = ConfigDefaults.Fresh("awg1");

        Assert.Equal("host-needed", ConfigRules.CheckReach(bare)!.Code);
        Assert.Equal("host-needed", ConfigRules.CheckReach(bare with { Host = "  " })!.Code);
        Assert.Null(ConfigRules.CheckReach(bare with { IsEnabled = false }));
        Assert.Null(ConfigRules.CheckReach(bare with { Host = "vpn.example.org" }));
    }

    [Fact]
    public void ASaveWithoutOpenedKeepsTheOneHeldAndANamedOneWins()
    {
        var held = ConfigDefaults.Fresh("awg1") with { Opened = true };

        Assert.True(ConfigAnswers.Draft(Endpoint(null), held).Opened);
        Assert.False(ConfigAnswers.Draft(Endpoint(false), held).Opened);
        Assert.False(ConfigAnswers.Draft(Endpoint(null)).Opened);
        Assert.True(ConfigAnswers.Draft(Endpoint(true)).Opened);
    }

    [Fact]
    public void ASaveOfThePanelWithoutOpenedKeepsTheOneHeld()
    {
        var held = PanelDefaults.Settings with { Opened = true };

        Assert.True(PanelAnswers.Draft(Panel(null), held).Opened);
        Assert.False(PanelAnswers.Draft(Panel(false), held).Opened);
        Assert.False(PanelAnswers.Draft(Panel(null)).Opened);
    }

    [Fact]
    public void ASaveOfTheSubscriptionsWithoutOpenedKeepsTheOneHeld()
    {
        var held = SubscriptionDefaults.Settings with { Separate = true, Port = 8444, Opened = true };

        Assert.True(SubscriptionAnswers.Draft(Subscriptions(null), held).Opened);
        Assert.False(SubscriptionAnswers.Draft(Subscriptions(false), held).Opened);
        Assert.False(SubscriptionAnswers.Draft(Subscriptions(null)).Opened);
    }

    [Fact]
    public void ANewEndpointTakesTheHostOfTheFirstEndpointThatNamesOne()
    {
        var configs = new[] { Held(3, "vpn3.example.org"), Held(1, " "), Held(2, "vpn2.example.org") };

        Assert.Equal("vpn2.example.org", ConfigHosts.Guess(configs, PanelDefaults.Settings, Root, "panel.example.net", ["8.8.8.8"]));
    }

    [Fact]
    public void ANewEndpointTakesTheDomainOfTheCertificateOfThePanelThenItsName()
    {
        var certified = PanelDefaults.Settings with { Certificate = $"{Root}/brandtned.ddns.net/fullchain.pem", Domains = ["vpn.example.org"] };
        var elsewhere = certified with { Certificate = "/opt/own/fullchain.pem" };
        var named = PanelDefaults.Settings with { Domains = ["panel.lan", "vpn.example.org"] };

        Assert.Equal("brandtned.ddns.net", ConfigHosts.Guess([], certified, Root, "127.0.0.1", []));
        Assert.Equal("vpn.example.org", ConfigHosts.Guess([], elsewhere, Root, "127.0.0.1", []));
        Assert.Equal("vpn.example.org", ConfigHosts.Guess([], named, Root, "127.0.0.1", []));
    }

    [Theory]
    [InlineData("brandtned.ddns.net", "brandtned.ddns.net")]
    [InlineData("157.228.132.111", "157.228.132.111")]
    [InlineData("127.0.0.1", "")]
    [InlineData("192.168.1.37", "")]
    [InlineData("localhost", "")]
    [InlineData("router.lan", "")]
    [InlineData("server", "")]
    public void ANewEndpointTakesTheHostTheRequestCameToWhenOutsidersReachIt(string asked, string host)
    {
        Assert.Equal(host, ConfigHosts.Guess([], PanelDefaults.Settings, Root, asked, []));
    }

    [Fact]
    public void ANewEndpointTakesAPublicIPv4OfTheHostLast()
    {
        string[] addresses = ["127.0.0.1", "10.0.0.5", "100.64.1.1", "169.254.3.3", "172.20.0.1", "192.0.2.7", "2a02:2d8::1", "157.228.132.111"];

        Assert.Equal("157.228.132.111", ConfigHosts.Guess([], PanelDefaults.Settings, Root, "127.0.0.1", addresses));
        Assert.Equal(string.Empty, ConfigHosts.Guess([], PanelDefaults.Settings, Root, "127.0.0.1", addresses[..^1]));
    }

    private static ServerConfig Held(long id, string host) => ConfigDefaults.Fresh($"awg{id}") with { Id = id, Host = host };

    private static ConfigRequest Endpoint(bool? opened) => new(
        Name: "awg1",
        Host: "vpn.example.org",
        ListenPort: 51820,
        Address: ["10.8.0.1/24"],
        Dns: null,
        AllowedIps: null,
        Mtu: 1280,
        Keepalive: 25,
        IsEnabled: true,
        Nat: true,
        Opened: opened,
        Blocked: null,
        PrivateKey: null,
        PresharedKey: null,
        Obfuscation: null);

    private static PanelRequest Panel(bool? opened) => new(null, null, 8443, opened, null, null, null, "ru", false);

    private static SubscriptionRequest Subscriptions(bool? opened) => new(true, true, null, null, 8444, opened, "sub", null, null, 12, null);
}
