using System.Security.Cryptography;
using System.Text.Json;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyOfferTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task OnlyTheEnabledSelectedProxiesArePublishedInTemplateOrder()
    {
        using var bench = new Bench();
        var first = Proxy("first", "websocket");
        var second = Proxy("second", "xhttp");
        var unselected = Proxy("unselected", "websocket");
        var disabled = Proxy("disabled", "websocket");
        disabled.IsEnabled = false;
        bench.Db.TransportProxies.AddRange(first, second, unselected, disabled);
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var template = new ClientTemplate { Name = "selected", ProxyIds = [second.Id, first.Id, second.Id, disabled.Id, 999] };
        var peer = Peer(template);
        var feature = Assert.IsType<ProxiesFeature>(await new ProxyOffer(bench.Scopes).OfferAsync(peer, default).ConfigureAwait(false));

        Assert.Equal([second.Id.ToString(), first.Id.ToString()], feature.Items.Select(item => item.Id));
        Assert.Equal("cdn.example.net", feature.Items[0].Host);
        Assert.Equal(443, feature.Items[0].Port);
        Assert.Equal(new ProxyXHttp("packet-up", "GET", "header"), feature.Items[0].Xhttp);
        Assert.Equal("vless", feature.Items[0].Auth.Scheme);
        Assert.Equal("bearer", feature.Items[1].Auth.Scheme);
        Assert.True(Guid.TryParse(feature.Source, out _));
        Assert.NotEqual(feature.Items[0].Auth.Credential, feature.Items[1].Auth.Credential);
        var text = JsonSerializer.Serialize(feature, Web);
        Assert.DoesNotContain("private-wg-key", text);
        Assert.DoesNotContain(first.Secret, text);
        Assert.DoesNotContain(first.CertificateKey, text);
        Assert.Contains("\"uplinkHTTPMethod\":\"GET\"", text);
    }

    [Fact]
    public async Task ATemplateWithNoSelectionPublishesAnEmptyCatalog()
    {
        using var bench = new Bench();
        bench.Db.TransportProxies.Add(Proxy("not-selected", "websocket"));
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var offer = new ProxyOffer(bench.Scopes);
        var first = Assert.IsType<ProxiesFeature>(await offer.OfferAsync(Peer(null), default).ConfigureAwait(false));
        var second = Assert.IsType<ProxiesFeature>(await offer.OfferAsync(Peer(new ClientTemplate { Name = "empty" }), default).ConfigureAwait(false));

        Assert.Empty(first.Items);
        Assert.Empty(second.Items);
        Assert.Equal(first.Source, second.Source);
    }

    [Fact]
    public async Task TheCatalogIdentityPersistsAcrossScopesAndDiffersBetweenServers()
    {
        using var one = new Bench();
        using var two = new Bench();
        var first = Assert.IsType<ProxiesFeature>(await new ProxyOffer(one.Scopes).OfferAsync(Peer(null), default).ConfigureAwait(false));
        var reread = Assert.IsType<ProxiesFeature>(await new ProxyOffer(one.Scopes).OfferAsync(Peer(null), default).ConfigureAwait(false));
        var another = Assert.IsType<ProxiesFeature>(await new ProxyOffer(two.Scopes).OfferAsync(Peer(null), default).ConfigureAwait(false));
        Assert.Equal(first.Source, reread.Source);
        Assert.NotEqual(first.Source, another.Source);
    }

    [Fact]
    public void TheCredentialFollowsTheClientAndProxySecretAlone()
    {
        var proxy = Proxy("selected", "xhttp");
        var first = TransportProxyStore.Credential(proxy, 7);
        Assert.Equal(first, TransportProxyStore.Credential(proxy, 7));
        Assert.NotEqual(first, TransportProxyStore.Credential(proxy, 8));
        Assert.True(Guid.TryParse(first, out _));
        proxy.Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Assert.NotEqual(first, TransportProxyStore.Credential(proxy, 7));
    }

    [Fact]
    public async Task TheLegacyWebSocketFeatureKeepsItsOriginalShape()
    {
        var peer = Peer(null);
        var feature = await new WebSocketOffer().OfferAsync(peer, default).ConfigureAwait(false);
        Assert.Equal(new WebSocketFeature(8443, "legacy-path"), feature);
        Assert.Equal("{\"port\":8443,\"path\":\"legacy-path\"}", JsonSerializer.Serialize(feature, Web));
    }

    [Fact]
    public async Task ATemplateRetainsItsProxySelectionInTheDatabase()
    {
        using var bench = new Bench();
        var first = Proxy("first", "websocket");
        var second = Proxy("second", "xhttp");
        bench.Db.TransportProxies.AddRange(first, second);
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var added = await bench.Templates.AddAsync(new ClientTemplate { Name = "selected", ProxyIds = [second.Id, first.Id, second.Id] }, default).ConfigureAwait(false);
        Assert.True(added.IsOk);
        var read = await bench.Templates.FindAsync(added.Record!.Id, default).ConfigureAwait(false);
        Assert.Equal([second.Id, first.Id], read!.ProxyIds);
    }

    [Fact]
    public async Task UpgradingTheOldSchemaRetainsTheExistingWebSocketAndClientKeys()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with
        {
            Host = "origin.example.net", WebSocket = true, ServicesPort = 8443, Address = ["10.77.0.1/24"],
        }, default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone") with
        {
            Address = ["10.77.0.2/32"],
        }, default).ConfigureAwait(false)).Record!;
        await bench.Db.GetService<IMigrator>().MigrateAsync("ConfigRequireUpdate").ConfigureAwait(false);
        bench.Db.ChangeTracker.Clear();
        await bench.Db.Database.MigrateAsync().ConfigureAwait(false);
        var readEndpoint = await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false);
        var readClient = await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false);
        Assert.True(readEndpoint!.WebSocket);
        Assert.Equal(endpoint.ServicesPort, readEndpoint.ServicesPort);
        Assert.Equal(endpoint.WebSocketPath, readEndpoint.WebSocketPath);
        Assert.Equal(endpoint.PrivateKey, readEndpoint.PrivateKey);
        Assert.Equal(client.PrivateKey, readClient!.PrivateKey);
        Assert.Empty(await bench.Db.TransportProxies.ToListAsync().ConfigureAwait(false));
    }

    private static TransportProxyEntity Proxy(string name, string transport) => new()
    {
        Name = name, Transport = transport, ProtocolVersion = transport == "xhttp" ? 2 : 0, Host = "cdn.example.net", Port = 443, Path = "transport",
        Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        CertificateKey = "/tls/private-server-key.pem", UpdatedUtc = DateTimeOffset.Parse("2026-11-04T10:00:00Z", null),
    };

    private static HelloPeer Peer(ClientTemplate? template) => new(
        new TunnelClient { Id = 7, Name = "phone", PrivateKey = "private-wg-key" },
        ConfigDefaults.Fresh("vpn") with { Host = "origin.example.net", ListenPort = 51820, ServicesPort = 8443, WebSocket = true, WebSocketPath = "legacy-path" },
        template, new DefaultHttpContext());
}
