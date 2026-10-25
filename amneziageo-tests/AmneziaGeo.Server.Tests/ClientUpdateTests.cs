using AmneziaGeo.Server.Api.ClientUpdates;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Status;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Updates;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Tests;

public sealed class ClientUpdateTests : IDisposable
{
    private static readonly PanelSettings Panel = PanelDefaults.Settings with { Path = "panel" };

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-client-updates-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void TheReleasesStartOffFromAStoreOfThePanel()
    {
        var settings = ClientUpdateDefaults.Settings;

        Assert.False(settings.IsEnabled);
        Assert.True(settings.Own);
        Assert.True(settings.Default);
        Assert.Equal(PanelDefaults.Port, settings.PortOr(PanelDefaults.Port));
        Assert.Equal("/", settings.Prefix);
        Assert.Null(ClientUpdateRules.Check(settings with { Path = "u7f3" }, Panel));
    }

    [Theory]
    [InlineData("api", 0, "updates-path-taken")]
    [InlineData("panel", 0, "updates-path-taken")]
    [InlineData("sub/x", 0, "updates-path-taken")]
    [InlineData("api", 9443, null)]
    [InlineData("", 0, "bad-updates-path")]
    [InlineData("../x", 0, "bad-updates-path")]
    [InlineData("up dates", 0, "bad-updates-path")]
    [InlineData("updates", 70000, "bad-port")]
    public void APathOfTheStoreMayNotMeetThePanelOnItsPort(string path, int port, string? code)
    {
        var settings = ClientUpdateDefaults.Settings with { IsEnabled = true, Path = path, Port = port };

        Assert.Equal(code, ClientUpdateRules.Check(settings, Panel)?.Code);
    }

    [Fact]
    public void TheDirectoryIsAFullPathOrNothing()
    {
        var settings = ClientUpdateDefaults.Settings with { IsEnabled = true, Path = "updates" };

        Assert.Null(ClientUpdateRules.Check(settings with { Directory = Path.GetTempPath() }, Panel));
        Assert.Equal("bad-updates-directory", ClientUpdateRules.Check(settings with { Directory = "releases" }, Panel)?.Code);
        Assert.Equal("bad-updates-directory", ClientUpdateRules.Check(settings with { Directory = Path.Combine(Path.GetTempPath(), "..", "x") }, Panel)?.Code);
    }

    [Theory]
    [InlineData("https://updates.example.net/", "secret", null)]
    [InlineData("https://updates.example.net/u/", "secret", null)]
    [InlineData("http://10.0.0.5:8444", "secret", null)]
    [InlineData("updates.example.net", "secret", "bad-updates-address")]
    [InlineData("ftp://updates.example.net", "secret", "bad-updates-address")]
    [InlineData("https://updates.example.net/?x=1", "secret", "bad-updates-address")]
    [InlineData("", "secret", "bad-updates-address")]
    [InlineData("https://updates.example.net/", "se cret", "bad-updates-token")]
    public void AnAddressIsAWebAddressWithoutAQuery(string address, string token, string? code)
    {
        var settings = ClientUpdateDefaults.Settings with { IsEnabled = true, Source = ClientUpdateDefaults.Address, Address = address, Token = token };

        Assert.Equal(code, ClientUpdateRules.Check(settings, Panel)?.Code);
        Assert.Equal("bad-updates-source", ClientUpdateRules.Check(settings with { Source = "github" }, Panel)?.Code);
    }

    [Fact]
    public void OnlyWhatMovesTheStoreBindsItAnew()
    {
        var settings = ClientUpdateDefaults.Settings with { IsEnabled = true, Path = "updates", Token = "t" };

        Assert.False(settings.Rebinds(settings with { Default = false }));
        Assert.False(settings.Rebinds(settings with { Path = "/updates/" }));
        Assert.True(settings.Rebinds(settings with { Path = "other" }));
        Assert.True(settings.Rebinds(settings with { Port = 8444 }));
        Assert.True(settings.Rebinds(settings with { Directory = "/srv/updates" }));
        Assert.True(settings.Rebinds(settings with { Token = "u" }));
        Assert.True(settings.Rebinds(settings with { IsEnabled = false }));
        Assert.True(settings.Rebinds(settings with { Source = ClientUpdateDefaults.Address, Address = "https://u.example.net" }));
    }

    [Fact]
    public async Task TheSettingsAreKeptInTheDatabase()
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database}").Options);
        await using (db)
        {
            await db.Database.MigrateAsync();
            var store = new ClientUpdateStore(db);

            var before = await store.ReadAsync(CancellationToken.None);
            var saved = await store.SaveAsync(
                new ClientUpdateSettings
                {
                    IsEnabled = true,
                    Source = ClientUpdateDefaults.Address,
                    Address = "https://updates.example.net/u",
                    Token = "secret",
                    Directory = "/srv/updates",
                    Port = 8444,
                    Path = "u7f3",
                    Default = false,
                },
                CancellationToken.None);
            var again = await store.ReadAsync(CancellationToken.None);

            Assert.Equal(ClientUpdateDefaults.Settings, before);
            Assert.Equal(saved, again);
            Assert.True(again.IsEnabled);
            Assert.False(again.Own);
            Assert.Equal("https://updates.example.net/u", again.Address);
            Assert.Equal("secret", again.Token);
            Assert.Equal("/srv/updates", again.Directory);
            Assert.Equal(8444, again.Port);
            Assert.Equal("u7f3", again.Path);
            Assert.False(again.Default);
        }
    }

    [Fact]
    public void TheHealthOfTheReleasesFollowsTheirSource()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z", null);
        var facts = new ServiceFacts(
            [],
            PanelDefaults.Port,
            SubscriptionDefaults.Settings with { IsEnabled = false },
            string.Empty,
            new Dictionary<int, string>(),
            new Dictionary<string, FrontFacts>(),
            true,
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<ServicePort, string> { [new ServicePort(FirewallPlan.Tcp, 8444)] = PortState.Closed },
            new ResolverFacts(false, false, 0, string.Empty, string.Empty),
            []);

        var off = ServiceChecks.Of(facts, now);
        var shared = ServiceChecks.Of(facts with { Updates = new ClientUpdateFacts(true, true, PanelDefaults.Port, true, string.Empty, string.Empty) }, now).Single();
        var apart = ServiceChecks.Of(facts with { Updates = new ClientUpdateFacts(true, true, 8444, false, string.Empty, string.Empty) }, now).Single();
        var refused = ServiceChecks.Of(facts with { Updates = new ClientUpdateFacts(true, true, 8445, false, "updates-port-busy", string.Empty) }, now).Single();
        var unwritable = ServiceChecks.Of(facts with { Updates = new ClientUpdateFacts(true, true, PanelDefaults.Port, true, "updates-directory", string.Empty) }, now).Single();
        var remote = ServiceChecks.Of(facts with { Updates = new ClientUpdateFacts(true, false, 0, false, string.Empty, "the service did not answer") }, now).Single();
        var ports = ServiceChecks.Exposed([], SubscriptionDefaults.Settings, new ClientUpdateFacts(true, true, 8444, false, string.Empty, string.Empty));

        Assert.Empty(off);
        Assert.Equal(ServiceChecks.Updates, shared.Kind);
        Assert.True(shared.Shared);
        Assert.True(shared.Works);
        Assert.Empty(shared.Ports);
        Assert.Equal([new ServicePort(FirewallPlan.Tcp, 8444)], apart.Ports);
        Assert.Equal(ServiceChecks.PortClosed, apart.Faults.Single().Code);
        Assert.Equal(ServiceChecks.PortRefused, refused.Faults.Single().Code);
        Assert.Equal(ServiceChecks.UpdatesDirectory, unwritable.Faults.Single().Code);
        Assert.Equal(ServiceChecks.UpdatesAddress, remote.Faults.Single().Code);
        Assert.Equal("the service did not answer", remote.Faults.Single().Detail);
        Assert.Equal("client updates", ServiceChecks.Label(remote));
        Assert.Equal([new ServicePort(FirewallPlan.Tcp, 8444)], ports);
    }

    [Fact]
    public void TheClientsReadTheReleasesAtTheHostTheyAsked()
    {
        var own = ClientUpdateDefaults.Settings with { IsEnabled = true, Path = "u7f3" };
        var apart = own with { Port = 8444 };
        var remote = own with { Source = ClientUpdateDefaults.Address, Address = "https://updates.example.net/u" };

        Assert.Equal("https://vpn.example.net:8443/u7f3/", ClientUpdateAnswers.Url(own, PanelDefaults.Settings, "vpn.example.net"));
        Assert.Equal("https://[2001:db8::1]:8444/u7f3/", ClientUpdateAnswers.Url(apart, PanelDefaults.Settings, "2001:db8::1"));
        Assert.Equal("https://updates.example.net/u/", ClientUpdateAnswers.Url(remote, PanelDefaults.Settings, "vpn.example.net"));
        Assert.Equal(string.Empty, ClientUpdateAnswers.Url(own with { IsEnabled = false }, PanelDefaults.Settings, "vpn.example.net"));
    }

    [Fact]
    public void ADraftKeepsTheTokenItIsNotGiven()
    {
        var held = ClientUpdateDefaults.Settings with { Token = "kept", Path = "u7f3" };

        var draft = ClientUpdateAnswers.Draft(new ClientUpdateRequest(true, null, null, null, " /srv/updates ", null, " /u7f3/ ", null), held);
        var replaced = ClientUpdateAnswers.Draft(new ClientUpdateRequest(true, "address", " https://u.example.net ", " new ", null, 8444, null, false), held);

        Assert.True(draft.IsEnabled);
        Assert.True(draft.Own);
        Assert.Equal("kept", draft.Token);
        Assert.Equal("/srv/updates", draft.Directory);
        Assert.Equal("u7f3", draft.Path);
        Assert.Equal(0, draft.Port);
        Assert.True(draft.Default);
        Assert.False(replaced.Own);
        Assert.Equal("https://u.example.net", replaced.Address);
        Assert.Equal("new", replaced.Token);
        Assert.Equal(8444, replaced.Port);
        Assert.False(replaced.Default);
    }

    [Fact]
    public async Task TheHelloNamesTheReleasesWhileTheyAreServed()
    {
        var state = new ClientUpdateState();
        var offer = new UpdatesOffer(state, PanelDefaults.Settings);
        var peer = Peer("vpn.example.net");
        var own = ClientUpdateDefaults.Settings with { IsEnabled = true, Path = "u7f3" };
        var pin = new string('a', 64);

        var off = await offer.OfferAsync(peer, CancellationToken.None);
        state.Current = own;
        var unserved = await offer.OfferAsync(peer, CancellationToken.None);
        state.Store = new ReleaseStore(_folder.FullName, TimeProvider.System);
        var served = await offer.OfferAsync(peer, CancellationToken.None);
        state.Current = own with { Source = ClientUpdateDefaults.Address, Address = "https://updates.example.net/u", Default = false };
        state.Pin = pin;
        var apart = await offer.OfferAsync(peer, CancellationToken.None);

        Assert.Equal(FeatureNames.Updates, offer.Name);
        Assert.Null(off);
        Assert.Null(unserved);
        Assert.Equal(new UpdatesFeature("https://vpn.example.net:8443/u7f3/", true, string.Empty), served);
        Assert.Equal(new UpdatesFeature("https://updates.example.net/u/", false, pin), apart);
    }

    // A client of an endpoint that asked the hello through the host given.
    private static HelloPeer Peer(string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host, PanelDefaults.Port);

        return new HelloPeer(ClientDefaults.Fresh(1, "milena"), ConfigDefaults.Fresh("awg1"), null, context);
    }
}
