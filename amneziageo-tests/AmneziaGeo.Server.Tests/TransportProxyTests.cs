using System.Text.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Carrier;
using AmneziaGeo.Server.Routing.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class TransportProxyTests
{
    [Fact]
    public async Task LegacyMigrationPreservesKeysAndDoesNotRecreateADeletedProxy()
    {
        using var bench = new Bench();
        var config = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with
        {
            Host = "origin.example.net", WebSocket = true, ServicesPort = 8443,
        }, default).ConfigureAwait(false)).Record!;
        var template = (await bench.Templates.AddAsync(new ClientTemplate { Name = "selected" }, default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(config.Id, "phone") with
        {
            TemplateId = template.Id,
        }, default).ConfigureAwait(false)).Record!;
        var store = new TransportProxyStore(bench.Db);
        await store.MigrateLegacyAsync(default).ConfigureAwait(false);
        await store.MigrateLegacyAsync(default).ConfigureAwait(false);
        var proxy = Assert.Single(await store.ListAsync(default).ConfigureAwait(false));
        Assert.Equal(config.Id, proxy.LegacyConfigId);
        Assert.Equal(config.Host, proxy.Host);
        Assert.Equal(config.ServicesPort, proxy.Port);
        Assert.Equal(config.WebSocketPath, proxy.Path);
        Assert.Equal([proxy.Id], (await bench.Templates.FindAsync(template.Id, default).ConfigureAwait(false))!.ProxyIds);
        Assert.Equal(client.PrivateKey, (await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false))!.PrivateKey);
        Assert.Equal(config.PrivateKey, (await bench.Configs.FindAsync(config.Id, default).ConfigureAwait(false))!.PrivateKey);
        await store.RemoveAsync(proxy.Id, default).ConfigureAwait(false);
        await store.MigrateLegacyAsync(default).ConfigureAwait(false);
        Assert.Empty(await store.ListAsync(default).ConfigureAwait(false));
        Assert.Empty((await bench.Templates.FindAsync(template.Id, default).ConfigureAwait(false))!.ProxyIds);
        Assert.False((await bench.Configs.FindAsync(config.Id, default).ConfigureAwait(false))!.WebSocket);
    }

    [Fact]
    public async Task NullTemplateClientsKeepTheirOriginalNetworkSettings()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with
        {
            Host = "origin.example.net", WebSocket = true,
        }, default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone"), default).ConfigureAwait(false)).Record!;
        var text = ClientText.Text(endpoint, client);
        await new TransportProxyStore(bench.Db).MigrateLegacyAsync(default).ConfigureAwait(false);
        var read = (await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false))!;
        Assert.Null(read.TemplateId);
        Assert.Equal(text, ClientText.Text((await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!, read));
    }

    [Fact]
    public async Task ASnapshotUsesCurrentTemplateAndClientAuthorization()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default).ConfigureAwait(false)).Record!;
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, Draft("selected", 8443, "relay"), default).ConfigureAwait(false);
        var template = (await bench.Templates.AddAsync(new ClientTemplate { Name = "selected", ProxyIds = [proxy.Id] }, default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone") with { TemplateId = template.Id }, default).ConfigureAwait(false)).Record!;
        Assert.Equal([TransportProxyStore.Credential(proxy, client.Id)], await store.CredentialsAsync(proxy, default).ConfigureAwait(false));
        await bench.Templates.ChangeAsync(template.Id, template with { ProxyIds = [] }, default).ConfigureAwait(false);
        Assert.Empty(await store.CredentialsAsync(proxy, default).ConfigureAwait(false));
    }

    [Fact]
    public async Task DifferentPathsShareAPortAndAHostDoesNotBypassAPathCollision()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var first = await store.SaveAsync(0, Draft("first", 8443, "relay"), default).ConfigureAwait(false);
        var rules = new ProxyRules(bench.Db);
        Assert.Null(await rules.ConflictAsync(8443, "another", 0, null, default).ConfigureAwait(false));
        Assert.Equal("proxy first", await rules.ConflictAsync(8443, "/RELAY/", 0, null, default).ConfigureAwait(false));
        Assert.Null(await rules.ConflictAsync(8443, "relay", first.Id, null, default).ConfigureAwait(false));
        Assert.Null(await rules.ConflictAsync(9443, "relay", 0, null, default).ConfigureAwait(false));
        var anotherHost = Draft("second", 8443, "another");
        anotherHost.Host = "cdn.example.net";
        Assert.Null(ProxyRules.Check(anotherHost));
    }

    [Fact]
    public void AnOlderTemplateRequestDoesNotEraseTheProxySelection()
    {
        var held = new ClientTemplate { Name = "selected", ProxyIds = [3, 8] };
        var old = new TemplateRequest("selected", [], [], null, null);
        Assert.Equal(held.ProxyIds, TemplateAnswers.Change(old, held).ProxyIds);
        Assert.Empty(TemplateAnswers.Change(old with { ProxyIds = [] }, held).ProxyIds);
    }

    [Fact]
    public void AnExplicitTargetTravelsInTheWstunnelTokenAndLegacyStillUsesLoopback()
    {
        var front = new WsEndpoint("proxy.example.net", 443, "relay", "");
        var modern = WsCarrier.Handshake(front, 51820, "key", "Bearer credential", "vpn.example.net");
        var legacy = WsCarrier.Handshake(front, 51820, "key", null);
        Assert.Contains("X-AmneziaGeo-Target: vpn.example.net:51820\r\n", modern);
        Assert.Equal("vpn.example.net", Target(modern));
        Assert.Equal("127.0.0.1", Target(legacy));
        Assert.DoesNotContain("X-AmneziaGeo-Target", legacy);
        Assert.Contains("Authorization: Bearer credential\r\n", modern);
        Assert.DoesNotContain("127.0.0.0/8", ProxyFile.Whitelist(0, "relay"));
        Assert.Contains("127.0.0.0/8", ProxyFile.Whitelist(51820, "relay"));
    }

    [Fact]
    public async Task ProxyPortsJoinTheSharedFirewallPlanWithoutOpeningUdp()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var first = Draft("first", 18443, "first");
        first.Opened = true;
        var saved = await store.SaveAsync(0, first, default).ConfigureAwait(false);
        var second = Draft("second", 18443, "second");
        second.Opened = true;
        await store.SaveAsync(0, second, default).ConfigureAwait(false);
        var applier = new FirewallApplier(bench.Configs, bench.Panel,
            bench.Services.GetRequiredService<SubscriptionStore>(), bench.Resolver, new DnsState(),
            new FirewallHost(new Tools(), new Ledger()), NullLogger<FirewallApplier>.Instance, store);
        var plan = await applier.PlanAsync(default).ConfigureAwait(false);
        var port = Assert.Single(plan.Ports);
        Assert.Equal("tcp", port.Protocol);
        Assert.Equal(18443, port.Port);
        await store.RemoveAsync(saved.Id, default).ConfigureAwait(false);
        Assert.Single((await applier.PlanAsync(default).ConfigureAwait(false)).Ports);
    }

    [Fact]
    public async Task AProxyOnThePanelPortKeepsTlsAfterRestart()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        await store.SaveAsync(0, Draft("selected", PanelDefaults.Port, "relay"), default).ConfigureAwait(false);
        Assert.True(PanelStore.ServesOn(bench.DatabasePath, PanelDefaults.Port));
        Assert.True(await bench.Configs.ServesAsync(PanelDefaults.Port, default).ConfigureAwait(false));
        var panel = PanelDefaults.Settings;
        var response = PanelAnswers.Panel(panel, panel, new WebOptions(), true, new PanelPlace(panel.Port, panel.Prefix, false));
        Assert.True(response.Pending);
    }

    private static string Target(string header)
    {
        var line = header.Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Protocol:", StringComparison.Ordinal));
        var token = line[(line.IndexOf("authorization.bearer.", StringComparison.Ordinal) + "authorization.bearer.".Length)..];
        var claims = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        claims = claims.PadRight((claims.Length + 3) / 4 * 4, '=');
        using var json = JsonDocument.Parse(Convert.FromBase64String(claims));
        return json.RootElement.GetProperty("r").GetString()!;
    }

    private static TransportProxyEntity Draft(string name, int port, string path) => new()
    {
        Name = name, Host = "proxy.example.net", Port = port, Path = path,
    };
}
