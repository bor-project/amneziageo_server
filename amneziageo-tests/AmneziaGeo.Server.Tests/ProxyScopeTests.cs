using System.Text.Json;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Proxy;
using AmneziaGeo.Server.Routing.XHttp;
using Microsoft.AspNetCore.Http;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyScopeTests
{
    [Theory]
    [InlineData("\"local\"")]
    [InlineData("null")]
    [InlineData("0")]
    public void InvalidLocalFlagNeverOpensTheProxy(string local)
    {
        var policy = ProxyTargetRules.Read("{\"restricted\":false,\"destinations\":[],\"local\":" + local + "}");
        Assert.False(ProxyTargetRules.Allows(policy, "external.example", 443));
    }

    [Fact]
    public void LocalPolicyRequiresExpansionAndCannotCarryExternalEntries()
    {
        Assert.True(ProxyTargetRules.TryNormalize(ProxyTargetRules.LocalOnly, out var local));
        Assert.Equal("local", ProxyTargetRules.Mode(local));
        Assert.False(ProxyTargetRules.Allows(local, "127.0.0.1", 51820));
        Assert.False(ProxyTargetRules.TryNormalize(new ProxyTargets(false, [], true), out _));
        Assert.False(ProxyTargetRules.TryNormalize(new ProxyTargets(true, [new("external.example", 443)], true), out _));
        Assert.Contains("allow: []", ProxyFile.Whitelist(0, "relay", local));
        Assert.Contains("allow: []", QuicHost.Rules([new(1, "relay", "quic", "proxy.example", 443, "relay", ["credential"], Targets: local)]));
        foreach (var method in new[] { "GET", "POST" })
        {
            using var rendered = JsonDocument.Parse(XHttpFile.Render(new XHttpProxy(1, 18443, "relay", [Guid.NewGuid().ToString()], local, method)));
            var settings = rendered.RootElement.GetProperty("httpGet");
            Assert.True(settings.GetProperty("restricted").GetBoolean());
            Assert.Empty(settings.GetProperty("destinations").EnumerateArray());
        }
    }

    [Fact]
    public void NewApiRequestsDefaultToLocalAndOldUpdatesKeepTheirPolicy()
    {
        var request = new ProxyRequest("relay", "websocket", "proxy.example", 443, "relay", true);
        Assert.Equal("local", ProxyTargetRules.Mode(ProxyTargetRules.Read(request.Draft().TargetsJson)));
        foreach (var policy in new ProxyTargets?[] { null, new(true, [new("external.example", 443)]), ProxyTargetRules.LocalOnly })
        {
            var held = new TransportProxyEntity { TargetsJson = ProxyTargetRules.Write(policy) };
            Assert.Equal(held.TargetsJson, request.Draft(held).TargetsJson);
        }
    }

    [Fact]
    public async Task LocalScopeTracksEnabledInterfacesAndDoesNotMixTheirPorts()
    {
        using var bench = new Bench();
        var firstResult = await bench.Configs.AddAsync(ConfigDefaults.Fresh("first") with
        {
            Host = "ONE.EXAMPLE", ListenPort = 45111, Address = ["10.77.1.1/24", "fd77::1/64"],
        }, default).ConfigureAwait(false);
        Assert.True(firstResult.IsOk, firstResult.Message);
        var first = firstResult.Record!;
        var second = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("second") with
        {
            Host = "two.example", ListenPort = 45112, Address = ["10.77.2.1/24"],
        }, default).ConfigureAwait(false)).Record!;
        await bench.Configs.AddAsync(ConfigDefaults.Fresh("off") with
        {
            Host = "off.example", ListenPort = 45113, IsEnabled = false,
        }, default).ConfigureAwait(false);
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, Draft("websocket", ProxyTargetRules.LocalOnly), default).ConfigureAwait(false);
        var credential = TransportProxyStore.Credential(proxy, 7);
        var before = await store.TargetsAsync(proxy, default).ConfigureAwait(false);
        Assert.True(ProxyTargetRules.Allows(before, "one.example", first.ListenPort));
        Assert.True(ProxyTargetRules.Allows(before, "two.example", second.ListenPort));
        Assert.True(ProxyTargetRules.Allows(before, "fd77::1", first.ListenPort));
        Assert.True(ProxyTargetRules.Allows(before, "10.77.1.1", first.ListenPort));
        Assert.True(ProxyTargetRules.Allows(before, "127.0.0.1", first.ListenPort));
        Assert.False(ProxyTargetRules.Allows(before, "one.example", second.ListenPort));
        Assert.False(ProxyTargetRules.Allows(before, "off.example", 45113));
        Assert.False(ProxyTargetRules.Allows(before, "external.example", first.ListenPort));

        var row = await bench.Db.Configs.FindAsync(first.Id).ConfigureAwait(false);
        row!.IsEnabled = false;
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var after = await store.TargetsAsync(proxy, default).ConfigureAwait(false);
        Assert.False(ProxyTargetRules.Allows(after, "one.example", first.ListenPort));
        Assert.False(ProxyTargetRules.Allows(after, "127.0.0.1", first.ListenPort));
        Assert.True(ProxyTargetRules.Allows(after, "two.example", second.ListenPort));
        Assert.Equal(credential, TransportProxyStore.Credential((await store.FindAsync(proxy.Id, default).ConfigureAwait(false))!, 7));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectedAndAnyKeepTheirExistingMeaning(bool restricted)
    {
        using var bench = new Bench();
        await bench.Configs.AddAsync(ConfigDefaults.Fresh("local") with { Host = "local.example", ListenPort = 45120 }, default).ConfigureAwait(false);
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, Draft("websocket", new(restricted, [new("remote.example", 443)])), default).ConfigureAwait(false);
        var policy = await store.TargetsAsync(proxy, default).ConfigureAwait(false);
        Assert.Equal(restricted ? "selected" : "any", ProxyTargetRules.Mode(policy));
        Assert.True(ProxyTargetRules.Allows(policy, "remote.example", 443));
        Assert.Equal(!restricted, ProxyTargetRules.Allows(policy, "local.example", 45120));
        Assert.Equal(!restricted, ProxyTargetRules.Allows(policy, "remote.example", 444));
    }

    [Fact]
    public async Task MissingOrExcessiveLocalDestinationsNeverBecomeAny()
    {
        using var bench = new Bench();
        var proxy = Draft("websocket", ProxyTargetRules.LocalOnly);
        var empty = await new TransportProxyStore(bench.Db).TargetsAsync(proxy, default).ConfigureAwait(false);
        Assert.False(ProxyTargetRules.Allows(empty, "external.example", 443));
        var excessive = ProxyTargetRules.Expand(ProxyTargetRules.LocalOnly,
            [.. Enumerable.Range(1, 65).Select(port => new ProxyDestination("127.0.0.1", port))]);
        Assert.False(ProxyTargetRules.Allows(excessive, "127.0.0.1", 1));
    }

    [Theory]
    [InlineData("websocket", "GET")]
    [InlineData("http", "GET")]
    [InlineData("http", "POST")]
    [InlineData("quic", "GET")]
    public async Task HelloPublishesModeAndConcreteTargetsForBothContracts(string transport, string method)
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("local") with
        {
            Host = "vpn.example", ListenPort = 45120,
        }, default).ConfigureAwait(false)).Record!;
        var store = new TransportProxyStore(bench.Db);
        var draft = Draft(transport, ProxyTargetRules.LocalOnly);
        draft.HttpMethod = method;
        var proxy = await store.SaveAsync(0, draft, default).ConfigureAwait(false);
        var peer = new HelloPeer(new TunnelClient { Id = 7, Name = "phone" }, endpoint,
            new ClientTemplate { Name = "selected", ProxyIds = [proxy.Id] }, new DefaultHttpContext());
        var modern = Assert.IsType<ProxiesFeatureV2>(await new ProxyOffer(bench.Scopes, true).OfferAsync(peer, default).ConfigureAwait(false));
        var item = Assert.Single(modern.Items);
        Assert.Equal("local", item.Scope);
        Assert.False(item.Targets!.Local);
        Assert.True(ProxyTargetRules.Allows(item.Targets, endpoint.Host, endpoint.ListenPort));
        Assert.False(ProxyTargetRules.Allows(item.Targets, "external.example", endpoint.ListenPort));
        var old = Assert.IsType<ProxiesFeature>(await new ProxyOffer(bench.Scopes).OfferAsync(peer, default).ConfigureAwait(false));
        if (transport == "websocket" || transport == "http" && method == "GET")
        {
            var legacy = Assert.Single(old.Items);
            Assert.Equal("local", legacy.Scope);
            Assert.Equal(item.Targets.Destinations, legacy.Targets!.Destinations);
        }
        else
        {
            Assert.Empty(old.Items);
        }

        proxy.TargetsJson = ProxyTargetRules.Write(new ProxyTargets(true, [new("remote.example", 443)]));
        await store.SaveAsync(proxy.Id, proxy, default).ConfigureAwait(false);
        var changed = Assert.IsType<ProxiesFeatureV2>(await new ProxyOffer(bench.Scopes, true).OfferAsync(peer, default).ConfigureAwait(false));
        var selected = Assert.Single(changed.Items);
        Assert.Equal("selected", selected.Scope);
        Assert.True(ProxyTargetRules.Allows(selected.Targets, "remote.example", 443));
        Assert.False(ProxyTargetRules.Allows(selected.Targets, endpoint.Host, endpoint.ListenPort));
        Assert.Equal(modern.Source, changed.Source);
        Assert.Equal(item.Auth, selected.Auth);
    }

    private static TransportProxyEntity Draft(string transport, ProxyTargets? targets) => new()
    {
        Hosted = false, Name = "relay", Transport = transport, Host = "proxy.example", Port = 443, Path = "relay",
        ProtocolVersion = transport == "http" ? 2 : 0, TargetsJson = ProxyTargetRules.Write(targets),
    };
}
