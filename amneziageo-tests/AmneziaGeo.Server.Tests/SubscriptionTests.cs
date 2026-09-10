using System.Text;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http;

namespace AmneziaGeo.Server.Tests;

public class SubscriptionTests
{
    [Fact]
    public void TheSubscriptionsStartOffOnAPortOfTheirOwn()
    {
        var settings = SubscriptionDefaults.Settings;

        Assert.False(settings.IsEnabled);
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
        var settings = SubscriptionDefaults.Settings with { Port = panel.Port, Path = path };

        Assert.Equal("subscription-path-taken", SubscriptionRules.Check(settings, panel)?.Code);
        Assert.Null(SubscriptionRules.Check(settings with { Port = 2096 }, panel));
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
        var settings = SubscriptionDefaults.Settings with { Certificate = "/srv/chain.pem" };

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
    public void TheAddressTakesThePortAndTheCertificateOfTheSubscriptions()
    {
        var settings = SubscriptionDefaults.Settings with { IsEnabled = true };
        var own = settings with { Certificate = "/srv/chain.pem", CertificateKey = "/srv/key.pem" };

        Assert.Equal(
            "https://10.0.0.1:2096/sub/abc",
            SubscriptionAnswer.Address(settings, PanelDefaults.Settings, true, "10.0.0.1", "abc"));
        Assert.Equal(
            "http://10.0.0.1:2096/sub/abc",
            SubscriptionAnswer.Address(settings, PanelDefaults.Settings, false, "10.0.0.1", "abc"));
        Assert.Equal(
            "https://10.0.0.1:2096/sub/abc",
            SubscriptionAnswer.Address(own, PanelDefaults.Settings, false, "10.0.0.1", "abc"));
    }

    [Fact]
    public void TheAddressOnThePortOfThePanelTakesItsCertificateAndName()
    {
        var settings = SubscriptionDefaults.Settings with
        {
            IsEnabled = true,
            Port = PanelDefaults.Port,
            Certificate = "/srv/chain.pem",
            CertificateKey = "/srv/key.pem",
        };
        var panel = PanelDefaults.Settings with { Domains = ["panel.example.org"] };

        Assert.Equal(
            "http://panel.example.org:8443/sub/abc",
            SubscriptionAnswer.Address(settings, panel, false, "10.0.0.1", "abc"));
    }

    [Fact]
    public void TheAddressTakesTheFirstNameAndLeavesTheUsualPortOut()
    {
        var named = SubscriptionDefaults.Settings with
        {
            IsEnabled = true,
            Port = 443,
            Domains = ["sub.example.org", "vpn.example.org"],
        };
        var plain = SubscriptionDefaults.Settings with { IsEnabled = true };

        Assert.Equal(
            "https://sub.example.org/sub/abc",
            SubscriptionAnswer.Address(named, PanelDefaults.Settings, true, "10.0.0.1", "abc"));
        Assert.Equal(
            "http://[fd00::1]:2096/sub/abc",
            SubscriptionAnswer.Address(plain, PanelDefaults.Settings, false, "fd00::1", "abc"));
        Assert.Equal(
            "http://[fd00::1]:2096/sub/abc",
            SubscriptionAnswer.Address(plain, PanelDefaults.Settings, false, "[fd00::1]", "abc"));
    }

    [Fact]
    public void NoAddressWhileTheSubscriptionsAreOffOrTheClientCarriesNone()
    {
        var on = SubscriptionDefaults.Settings with { IsEnabled = true };

        Assert.Empty(SubscriptionAnswer.Address(SubscriptionDefaults.Settings, PanelDefaults.Settings, true, "10.0.0.1", "abc"));
        Assert.Empty(SubscriptionAnswer.Address(on, PanelDefaults.Settings, true, "10.0.0.1", string.Empty));
    }

    [Fact]
    public void TheBodyCarriesTheLinksLineByLineInBase64()
    {
        var feed = new ClientFeed(["vpn://one", "vpn://two"], 10, 20);

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

        var feed = ClientFeed.Of([on, off], members, new Dictionary<long, ClientTemplate>(), Counted);

        Assert.Equal([ClientLink.Link(on, milena)], feed.Links);
        Assert.Equal(5UL, feed.Upload);
        Assert.Equal(7UL, feed.Download);
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
                IsEnabled = true,
                Listen = ["127.0.0.1"],
                Domains = ["vpn.example.org"],
                Port = 2097,
                Path = "feed",
                UpdateHours = 6,
                Title = "Сервер",
            },
            CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);

        Assert.False(fresh.IsEnabled);
        Assert.True(read.IsEnabled);
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

    private static IReadOnlyList<ClientState> Counted(ServerConfig endpoint, IReadOnlyList<TunnelClient> clients) =>
        [.. clients.Select(one => new ClientState(one.Name, one.PublicKey, true, true, null, 5, 7, string.Empty))];

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
