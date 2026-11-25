using System.Net;
using System.Net.Http.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Awg.Uapi;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Panel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ClientPresharedTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExportAndKernelUpdateUseTheSameEffectiveKey(bool inherited, bool individual)
    {
        var endpoint = ConfigDefaults.Fresh("psk849") with { PresharedKey = inherited ? Key() : string.Empty };
        var client = ClientDefaults.Fresh(1, "phone") with { PresharedKey = individual ? Key() : string.Empty };
        var expected = individual ? client.PresharedKey : endpoint.PresharedKey;
        var peer = ClientDevice.Peer(endpoint, client);
        var requests = DeviceWriter.Requests(new AwgUpdate { Name = endpoint.Name, Peers = [peer] }, 41, () => 1);
        var attributes = NetlinkAttributes.Map(Assert.Single(requests).AsMemory(20));
        var nested = Assert.Single(NetlinkAttributes.Walk(attributes[(ushort)WgDeviceAttribute.Peers]));
        var fields = NetlinkAttributes.Map(nested.Item2);
        Assert.True(fields.ContainsKey((ushort)WgPeerAttribute.PresharedKey));
        var written = fields[(ushort)WgPeerAttribute.PresharedKey].ToArray();
        Assert.Equal(WgUapi.KeyLength, written.Length);
        Assert.Equal(expected.Length > 0 ? Convert.FromBase64String(expected) : new byte[WgUapi.KeyLength], written);
        var text = ClientText.Text(endpoint, client);
        if (expected.Length > 0)
        {
            Assert.Contains("PresharedKey = " + expected, text, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("PresharedKey =", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientEditorsSeeInheritanceWithoutReceivingTheInterfaceKey(bool shared)
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("pskrole849") with { PresharedKey = shared ? Key() : string.Empty }, default).ConfigureAwait(false)).Record!;
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, configure: web => web.Use(async (context, next) =>
        {
            context.Items["amneziageo.caller"] = new Principal(1, "client-editor", AuthScheme.Password, 1,
                new HashSet<string> { Scopes.ReadState, Scopes.ManageClients }, "editor");
            await next(context).ConfigureAwait(false);
        })).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var found = (await api.GetFromJsonAsync<ConfigResponse>($"/api/configs/{endpoint.Id}").ConfigureAwait(false))!;
        Assert.Equal(shared, found.HasPresharedKey);
        Assert.Null(found.PresharedKey);
        Assert.Null(found.PrivateKey);
        Assert.Equal(shared, Assert.Single((await api.GetFromJsonAsync<ConfigResponse[]>("/api/configs").ConfigureAwait(false))!).HasPresharedKey);
    }

    [Fact]
    public async Task SavingAnInterfaceRotatesInheritedKeysWithoutReplacingIndividualKeys()
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var endpoint = ConfigDefaults.Fresh("psk849") with { IsEnabled = false, Host = "vpn.example", Address = ["10.84.9.1/24"] };
        using var created = await api.PostAsJsonAsync("/api/configs", Request(endpoint)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var config = (await created.Content.ReadFromJsonAsync<ConfigResponse>().ConfigureAwait(false))!;
        var inherited = (await bench.Clients.AddAsync(ClientDefaults.Fresh(config.Id, "inherited"), default).ConfigureAwait(false)).Record!;
        var individual = (await bench.Clients.AddAsync(ClientDefaults.Fresh(config.Id, "individual") with { PresharedKey = Key() }, default).ConfigureAwait(false)).Record!;
        foreach (var next in new[] { Key(), Key(), string.Empty })
        {
            var request = Request(endpoint with { PresharedKey = next });
            using var preview = await api.PostAsJsonAsync($"/api/configs/{config.Id}/preview", request).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var impact = (await preview.Content.ReadFromJsonAsync<ConfigChangePreview>().ConfigureAwait(false))!;
            var changed = Assert.Single(impact.Clients);
            Assert.Equal(inherited.Id, changed.Id);
            Assert.Equal(["[peer]1.presharedkey"], changed.Fields);
            Assert.True(changed.RequiresCoordination);
            using var saved = await api.PutAsJsonAsync($"/api/configs/{config.Id}", request).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var actual = (await bench.Configs.FindAsync(config.Id, default).ConfigureAwait(false))!;
            var member = (await bench.Clients.FindAsync(inherited.Id, default).ConfigureAwait(false))!;
            Assert.Equal(string.Empty, member.PresharedKey);
            Assert.Equal(individual.PresharedKey, (await bench.Clients.FindAsync(individual.Id, default).ConfigureAwait(false))!.PresharedKey);
            Assert.Equal(next, ClientText.Preshared(actual, member));
            Assert.Equal(individual.PresharedKey, ClientText.Preshared(actual, individual));
            foreach (var client in new[] { member, individual })
            {
                var exported = (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!;
                var expected = ClientText.Preshared(actual, client);
                Assert.Equal(expected.Length > 0, exported.Text.Contains("PresharedKey =", StringComparison.Ordinal));
                if (expected.Length > 0)
                {
                    Assert.Contains("PresharedKey = " + expected, exported.Text, StringComparison.Ordinal);
                }
            }
            var response = (await api.GetFromJsonAsync<ClientResponse>($"/api/clients/{member.Id}").ConfigureAwait(false))!;
            Assert.Empty(response.PresharedKey);
        }
    }

    [Fact]
    public async Task ADeferredPresharedKeyLeavesActiveExportsUntilApplication()
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var endpoint = ConfigDefaults.Fresh("psklater849") with { IsEnabled = false, Host = "vpn.example", PresharedKey = Key() };
        using var created = await api.PostAsJsonAsync("/api/configs", Request(endpoint)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var config = (await created.Content.ReadFromJsonAsync<ConfigResponse>().ConfigureAwait(false))!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(config.Id, "inherited"), default).ConfigureAwait(false)).Record!;
        var next = Key();
        using var saved = await api.PutAsJsonAsync($"/api/configs/{config.Id}", Request(endpoint with { PresharedKey = next }) with { Defer = true }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("PresharedKey = " + endpoint.PresharedKey, (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!.Text, StringComparison.Ordinal);
        using var applied = await api.PostAsync($"/api/configs/{config.Id}/pending/apply", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Contains("PresharedKey = " + next, (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!.Text, StringComparison.Ordinal);
        var added = (await bench.Clients.AddAsync(ClientDefaults.Fresh(config.Id, "new-client"), default).ConfigureAwait(false)).Record!;
        Assert.Empty(added.PresharedKey);
        Assert.Equal(next, ClientText.Preshared((await bench.Configs.FindAsync(config.Id, default).ConfigureAwait(false))!, added));
    }

    [Fact]
    public async Task HelloSubscriptionRevisionsFollowOnlyEffectivePresharedChanges()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("pskhello849") with { Host = "vpn.example", IsEnabled = true }, default).ConfigureAwait(false)).Record!;
        var inherited = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "inherited"), default).ConfigureAwait(false)).Record!;
        var individual = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "individual") with { PresharedKey = Key() }, default).ConfigureAwait(false)).Record!;
        var state = new SubscriptionState { Current = SubscriptionDefaults.Settings with { IsEnabled = true } };
        var offer = new SubscriptionOffer(state, PanelDefaults.Settings, new WebOptions(), bench.Scopes);
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("vpn.example");
        var before = await Offered(inherited, endpoint).ConfigureAwait(false);
        var overrideBefore = await Offered(individual, endpoint).ConfigureAwait(false);
        var changed = (await bench.Configs.ChangeAsync(endpoint.Id, endpoint with { PresharedKey = Key() }, null, default).ConfigureAwait(false)).Record!;
        var after = await Offered(inherited, changed).ConfigureAwait(false);
        var overrideAfter = await Offered(individual, changed).ConfigureAwait(false);
        Assert.NotEqual(before.ConfigurationRevision, after.ConfigurationRevision);
        Assert.NotEqual(before.Revision, after.Revision);
        Assert.Equal(overrideBefore.ConfigurationRevision, overrideAfter.ConfigurationRevision);
        Assert.Equal(ConfigurationRevision.Of(ClientText.Text(changed, inherited, await bench.Templates.EffectiveAsync(null, default).ConfigureAwait(false))), after.ConfigurationRevision);

        async Task<SubscriptionFeature> Offered(TunnelClient member, ServerConfig config) =>
            Assert.IsType<SubscriptionFeature>(await offer.OfferAsync(new HelloPeer(member, config, null, context), default).ConfigureAwait(false));
    }

    private static string Key() => Curve25519.Create().PrivateKey;

    private static ConfigRequest Request(ServerConfig config) => new(config.Name, config.Host, config.ListenPort,
        [.. config.Address], [.. config.Dns], [.. config.AllowedIps], config.Mtu, config.Keepalive, config.IsEnabled,
        config.Nat, false, [.. config.Blocked], config.PrivateKey, config.PresharedKey,
        ConfigAnswers.Obfuscation(config.Obfuscation, true), WebSocket: false, ServicesPort: config.ListenPort);
}
