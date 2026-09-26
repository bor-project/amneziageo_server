using System.Text;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http;

namespace AmneziaGeo.Server.Tests;

public class SubscriptionTests
{
    [Fact]
    public void TheSubscriptionsStartOnAtThePortsOfTheServices()
    {
        var settings = SubscriptionDefaults.Settings;

        Assert.True(settings.IsEnabled);
        Assert.False(settings.Separate);
        Assert.Equal(["*:2096"], settings.Entries);
        Assert.Equal("/sub/", settings.Prefix);
        Assert.Equal(12, settings.UpdateHours);
        Assert.Null(SubscriptionRules.Check(settings, PanelDefaults.Settings));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("sub page")]
    [InlineData("../sub")]
    public void APathThatIsNotOneIsRefused(string path)
    {
        var settings = SubscriptionDefaults.Settings with { Path = path };

        Assert.Equal("bad-subscription-path", SubscriptionRules.Check(settings, PanelDefaults.Settings)?.Code);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("assets/sub")]
    [InlineData("panel/sub")]
    public void APathThePanelAnswersUnderIsRefusedOnItsPort(string path)
    {
        var panel = PanelDefaults.Settings with { Path = "panel" };
        var settings = SubscriptionDefaults.Settings with { Separate = true, Port = panel.Port, Path = path };

        Assert.Equal("subscription-path-taken", SubscriptionRules.Check(settings, panel)?.Code);
        Assert.Null(SubscriptionRules.Check(settings with { Port = 2096 }, panel));
    }

    [Theory]
    [InlineData("api", "subscription-path-taken")]
    [InlineData("v1/sub", "subscription-path-taken")]
    [InlineData("sub", null)]
    [InlineData("panel", null)]
    public void APathTheServicesAnswerUnderIsRefusedOnTheirPorts(string path, string? code)
    {
        var settings = SubscriptionDefaults.Settings with { Path = path };

        Assert.Equal(code, SubscriptionRules.Check(settings, PanelDefaults.Settings with { Path = "panel" })?.Code);
    }

    [Fact]
    public void TheCertificateAndThePortOfTheirOwnCountOnlyApart()
    {
        var settings = SubscriptionDefaults.Settings with { Port = PanelDefaults.Port, Path = "api", Certificate = "/srv/chain.pem" };

        Assert.Equal("subscription-path-taken", SubscriptionRules.Check(settings, PanelDefaults.Settings)?.Code);
        Assert.Null(SubscriptionRules.Check(settings with { Path = "sub" }, PanelDefaults.Settings));
        Assert.Equal("bad-certificate", SubscriptionRules.Check(settings with { Path = "sub", Separate = true }, PanelDefaults.Settings)?.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public void AnIntervalOutsideTheRangeIsRefused(int hours)
    {
        var settings = SubscriptionDefaults.Settings with { UpdateHours = hours };

        Assert.Equal("bad-subscription-interval", SubscriptionRules.Check(settings, PanelDefaults.Settings)?.Code);
    }

    [Fact]
    public void ATitleTooLongOrBreakingTheLineIsRefused()
    {
        var broken = SubscriptionDefaults.Settings with { Title = "one\ntwo" };
        var long_ = SubscriptionDefaults.Settings with { Title = new string('a', 129) };

        Assert.Equal("bad-subscription-title", SubscriptionRules.Check(broken, PanelDefaults.Settings)?.Code);
        Assert.Equal("bad-subscription-title", SubscriptionRules.Check(long_, PanelDefaults.Settings)?.Code);
    }

    [Fact]
    public void ACertificateTakesBothPaths()
    {
        var settings = SubscriptionDefaults.Settings with { Separate = true, Certificate = "/srv/chain.pem" };

        Assert.Equal("bad-certificate", SubscriptionRules.Check(settings, PanelDefaults.Settings)?.Code);
    }

    [Fact]
    public void OnlyTheListenAndTheCertificateBindTheSubscriptionsAnew()
    {
        var settings = SubscriptionDefaults.Settings with { IsEnabled = true };

        Assert.False(settings.Rebinds(settings with
        {
            Path = "feed",
            Title = "vpn",
            UpdateHours = 6,
            Domains = ["vpn.example.org"],
        }));
        Assert.True(settings.Rebinds(settings with { Port = 2097 }));
        Assert.True(settings.Rebinds(settings with { Listen = ["127.0.0.1"] }));
        Assert.True(settings.Rebinds(settings with { Certificate = "/srv/chain.pem", CertificateKey = "/srv/key.pem" }));
        Assert.True(settings.Rebinds(settings with { IsEnabled = false }));
        Assert.True(settings.Rebinds(settings with { Separate = true }));
    }

    [Theory]
    [InlineData("/sub/abc123", "abc123")]
    [InlineData("/sub/", null)]
    [InlineData("/sub/abc/def", null)]
    [InlineData("/feed/abc", null)]
    [InlineData("/sub/abc def", null)]
    public void APathNamesTheSubscriptionAfterThePrefix(string path, string? id)
    {
        Assert.Equal(id, SubscriptionAnswer.Asked(new PathString(path), SubscriptionDefaults.Settings));
    }

    [Fact]
    public void TheAddressTakesThePortOfTheServicesOfTheEndpointAndItsHost()
    {
        var settings = SubscriptionDefaults.Settings;
        var bare = Endpoint(1, "awg1") with { Host = string.Empty, ListenPort = 51820 };

        Assert.Equal(
            "https://vpn.example.org:51820/sub/abc",
            SubscriptionAnswer.Address(settings, PanelDefaults.Settings, false, "10.0.0.1", Endpoint(1, "awg1") with { ListenPort = 51820 }, "abc"));
        Assert.Equal(
            "https://10.0.0.1:51820/sub/abc",
            SubscriptionAnswer.Address(settings, PanelDefaults.Settings, false, "10.0.0.1", bare, "abc"));
        Assert.Equal(
            "https://panel.example.org:51820/feed/abc",
            SubscriptionAnswer.Address(
                settings with { Path = "feed" },
                PanelDefaults.Settings with { Domains = ["panel.example.org"] },
                true,
                "10.0.0.1",
                bare,
                "abc"));
    }

    [Fact]
    public void TheAddressTakesThePortAndTheCertificateOfTheSubscriptions()
    {
        var settings = SubscriptionDefaults.Settings with { Separate = true };
        var own = settings with { Certificate = "/srv/chain.pem", CertificateKey = "/srv/key.pem" };

        Assert.Equal(
            "https://10.0.0.1:2096/sub/abc",
            SubscriptionAnswer.Address(settings, PanelDefaults.Settings, true, "10.0.0.1", Endpoint(1, "awg1"), "abc"));
        Assert.Equal(
            "http://10.0.0.1:2096/sub/abc",
            SubscriptionAnswer.Address(settings, PanelDefaults.Settings, false, "10.0.0.1", Endpoint(1, "awg1"), "abc"));
        Assert.Equal(
            "https://10.0.0.1:2096/sub/abc",
            SubscriptionAnswer.Address(own, PanelDefaults.Settings, false, "10.0.0.1", Endpoint(1, "awg1"), "abc"));
    }

    [Fact]
    public void TheAddressOnThePortOfThePanelTakesItsCertificateAndName()
    {
        var settings = SubscriptionDefaults.Settings with
        {
            Separate = true,
            Port = PanelDefaults.Port,
            Certificate = "/srv/chain.pem",
            CertificateKey = "/srv/key.pem",
        };
        var panel = PanelDefaults.Settings with { Domains = ["panel.example.org"] };

        Assert.Equal(
            "http://panel.example.org:8443/sub/abc",
            SubscriptionAnswer.Address(settings, panel, false, "10.0.0.1", Endpoint(1, "awg1"), "abc"));
    }

    [Fact]
    public void TheAddressTakesTheFirstNameAndLeavesTheUsualPortOut()
    {
        var named = SubscriptionDefaults.Settings with
        {
            Separate = true,
            Port = 443,
            Domains = ["sub.example.org", "vpn.example.org"],
        };
        var plain = SubscriptionDefaults.Settings with { Separate = true };
        var endpoint = Endpoint(1, "awg1");

        Assert.Equal(
            "https://sub.example.org/sub/abc",
            SubscriptionAnswer.Address(named, PanelDefaults.Settings, true, "10.0.0.1", endpoint, "abc"));
        Assert.Equal(
            "http://[fd00::1]:2096/sub/abc",
            SubscriptionAnswer.Address(plain, PanelDefaults.Settings, false, "fd00::1", endpoint, "abc"));
        Assert.Equal(
            "http://[fd00::1]:2096/sub/abc",
            SubscriptionAnswer.Address(plain, PanelDefaults.Settings, false, "[fd00::1]", endpoint, "abc"));
    }

    [Fact]
    public void NoAddressWhileTheSubscriptionsAreOffOrTheClientCarriesNone()
    {
        var off = SubscriptionDefaults.Settings with { IsEnabled = false };
        var endpoint = Endpoint(1, "awg1");

        Assert.Empty(SubscriptionAnswer.Address(off, PanelDefaults.Settings, true, "10.0.0.1", endpoint, "abc"));
        Assert.Empty(SubscriptionAnswer.Address(SubscriptionDefaults.Settings, PanelDefaults.Settings, true, "10.0.0.1", endpoint, string.Empty));
    }

    [Fact]
    public void TheRevisionMarksWhatTheSubscriptionHandsOut()
    {
        var one = SubscriptionAnswer.Revision("dnBuOi8vb25l");

        Assert.Matches("^[0-9a-f]{32}$", one);
        Assert.Equal(one, SubscriptionAnswer.Revision("dnBuOi8vb25l"));
        Assert.NotEqual(one, SubscriptionAnswer.Revision("dnBuOi8vdHdv"));
    }

    [Fact]
    public void TheBodyCarriesTheLinksLineByLineInBase64()
    {
        var feed = new ClientFeed(["vpn://one", "vpn://two"], 10, 20, 0);

        Assert.Equal("vpn://one\nvpn://two", Encoding.UTF8.GetString(Convert.FromBase64String(feed.Body)));
        Assert.Equal("upload=10; download=20; total=0; expire=0", feed.Usage);
        Assert.Equal("base64:0KHQtdGA0LLQtdGA", ClientFeed.Title("Сервер"));
    }

    [Fact]
    public void AFeedCarriesTheClientsThatAreOnAtEndpointsThatAreOn()
    {
        var on = Endpoint(1, "awg1");
        var off = Endpoint(2, "awg2") with { IsEnabled = false };
        var milena = Member(1, "milena");
        var members = new[]
        {
            milena,
            Member(1, "bogdan") with { IsEnabled = false },
            Member(1, "peer") with { PrivateKey = string.Empty },
            Member(2, "dima"),
        };

        var feed = ClientFeed.Of([on, off], members, new Dictionary<long, ClientTemplate>(), _ => new ClientUsage(5, 7));

        Assert.Equal([ClientLink.Link(on, milena)], feed.Links);
        Assert.Equal(5UL, feed.Upload);
        Assert.Equal(7UL, feed.Download);
    }

    [Fact]
    public void AFeedNamesTheConfigurationsAsItIsTold()
    {
        var on = Endpoint(1, "awg1");
        var milena = Member(1, "milena");

        var feed = ClientFeed.Of(
            [on],
            [milena],
            new Dictionary<long, ClientTemplate>(),
            _ => new ClientUsage(0, 0),
            title: (endpoint, member) => ClientText.Title(endpoint, member, "{CLIENT}@{INTERFACE}"));

        Assert.Equal([ClientLink.Link(on, milena, title: "milena@awg1")], feed.Links);
    }

    [Fact]
    public void AFeedAddsUpTheTrafficAndTheLimitsOfItsClients()
    {
        var on = Endpoint(1, "awg1");
        var milena = Member(1, "milena") with { Id = 1, DailyLimit = 100 };
        var phone = Member(1, "milena-phone") with { Id = 2, DailyLimit = 100 };
        var other = Member(1, "bogdan") with { Id = 3, DailyLimit = 50 };
        var templates = new Dictionary<long, ClientTemplate>();

        var feed = ClientFeed.Of([on], [milena, phone, other], templates, Used);
        var open = ClientFeed.Of([on], [milena, other with { DailyLimit = 0 }], templates, Used);

        Assert.Equal(21UL, feed.Upload);
        Assert.Equal(42UL, feed.Download);
        Assert.Equal(250UL, feed.Total);
        Assert.Equal("upload=21; download=42; total=250; expire=0", feed.Usage);
        Assert.Equal(0UL, open.Total);

        static ClientUsage Used(TunnelClient client) => client.Id == 3 ? new ClientUsage(1, 2) : new ClientUsage(10, 20);
    }

    [Fact]
    public async Task TheSettingsReadBackAsTheyWereSaved()
    {
        using var bench = new Bench();
        var store = new SubscriptionStore(bench.Db, bench.Clock);

        var fresh = await store.ReadAsync(CancellationToken.None);
        await store.SaveAsync(
            fresh with
            {
                IsEnabled = false,
                Separate = true,
                Listen = ["127.0.0.1"],
                Domains = ["vpn.example.org"],
                Port = 2097,
                Path = "feed",
                UpdateHours = 6,
                Title = "Сервер",
            },
            CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);

        Assert.True(fresh.IsEnabled);
        Assert.False(fresh.Separate);
        Assert.False(read.IsEnabled);
        Assert.True(read.Separate);
        Assert.Equal(["127.0.0.1"], read.Listen);
        Assert.Equal(["vpn.example.org"], read.Domains);
        Assert.Equal(2097, read.Port);
        Assert.Equal("/feed/", read.Prefix);
        Assert.Equal(6, read.UpdateHours);
        Assert.Equal("Сервер", read.Title);
    }

    [Fact]
    public async Task ASubscriptionCarriesTheClientsUnderItsName()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        await bench.Clients.AddAsync(Fresh(endpoint, "milena", "10.8.0.2/32") with { SubscriptionId = "family" }, CancellationToken.None);
        await bench.Clients.AddAsync(Fresh(endpoint, "bogdan", "10.8.0.3/32") with { SubscriptionId = "family" }, CancellationToken.None);
        await bench.Clients.AddAsync(Fresh(endpoint, "dima", "10.8.0.4/32"), CancellationToken.None);

        var family = await bench.Clients.SubscribedAsync("family", CancellationToken.None);

        Assert.Equal(["bogdan", "milena"], family.Select(one => one.Name));
        Assert.Empty(await bench.Clients.SubscribedAsync("Family", CancellationToken.None));
        Assert.Empty(await bench.Clients.SubscribedAsync(string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task AnImportedClientTakesASubscriptionOfItsOwn()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);

        var taken = await bench.Clients.ImportAsync(
            Fresh(endpoint, "milena", "10.8.0.2/32") with { SubscriptionId = string.Empty },
            CancellationToken.None);

        Assert.True(taken.IsOk, taken.Message);
        Assert.Matches("^[a-z0-9]{16}$", taken.Record!.SubscriptionId);
    }

    [Fact]
    public void AFreshClientCarriesASubscriptionOfItsOwn()
    {
        var one = ClientDefaults.Fresh(1, "milena");
        var other = ClientDefaults.Fresh(1, "bogdan");

        Assert.Matches("^[a-z0-9]{16}$", one.SubscriptionId);
        Assert.NotEqual(one.SubscriptionId, other.SubscriptionId);
    }

    [Theory]
    [InlineData("family one")]
    [InlineData("семья")]
    [InlineData("family\n")]
    public void ASubscriptionThatIsNotOneIsRefused(string id)
    {
        var client = ClientDefaults.Fresh(1, "milena") with { Address = ["10.8.0.2/32"], SubscriptionId = id };

        Assert.Equal("bad-client-subscription", ClientRules.Check(client)?.Code);
    }

    [Fact]
    public void ASubscriptionLongerThanItsLimitIsRefusedAndAnEmptyOneHolds()
    {
        var client = ClientDefaults.Fresh(1, "milena") with { Address = ["10.8.0.2/32"] };

        Assert.Equal("bad-client-subscription", ClientRules.Check(client with { SubscriptionId = new string('a', 65) })?.Code);
        Assert.Null(ClientRules.Check(client with { SubscriptionId = string.Empty }));
    }

    private static ServerConfig Endpoint(long id, string name) =>
        ConfigDefaults.Fresh(name) with { Id = id, Host = "vpn.example.org", Address = ["10.8.0.1/24"] };

    private static TunnelClient Member(long configId, string name) =>
        ClientDefaults.Fresh(configId, name) with { Address = ["10.8.0.2/32"], SubscriptionId = "family" };

    private static async Task<long> EndpointAsync(Bench bench)
    {
        var added = await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None);

        return added.Record!.Id;
    }

    private static TunnelClient Fresh(long endpoint, string name, string address) =>
        ClientDefaults.Fresh(endpoint, name) with { Address = [address] };
}
