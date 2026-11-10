using System.Text.Json;
using AmneziaGeo.Server.Api.Proxies;
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

public sealed class XHttpCatalogTests
{
    [Fact]
    public async Task PublicOfferAndTlsListenerKeepDifferentPorts()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        using var identity = new ProxyTlsFixture("cdn.example.net");
        var proxy = await store.SaveAsync(0, new TransportProxyEntity
        {
            Name = "CDN", Transport = "xhttp", ProtocolVersion = 2, Host = "cdn.example.net", Port = 443,
            ListenPort = 18080, OriginHttp = false, Path = "relay",
            Certificate = identity.Chain, CertificateKey = identity.Key, Pin = identity.Pin,
        }, default).ConfigureAwait(false);
        var feature = Assert.IsType<ProxiesFeature>(await new ProxyOffer(bench.Scopes).OfferAsync(new HelloPeer(
            new TunnelClient { Id = 7, Name = "client" }, ConfigDefaults.Fresh("vpn"),
            new ClientTemplate { Name = "selected", ProxyIds = [proxy.Id] }, new DefaultHttpContext()), default).ConfigureAwait(false));
        Assert.Equal(443, Assert.Single(feature.Items).Port);
        Assert.False(await store.ServesAsync(443, default).ConfigureAwait(false));
        Assert.True(await store.ServesAsync(18080, default).ConfigureAwait(false));
        Assert.Equal("proxy CDN", await new ProxyRules(bench.Db).ConflictAsync(18080, "relay", 0, null, default).ConfigureAwait(false));
        Assert.Null(await new ProxyRules(bench.Db).ConflictAsync(443, "relay", 0, null, default).ConfigureAwait(false));
        var text = JsonSerializer.Serialize(feature, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("listenPort", text);
        Assert.DoesNotContain("originHttp", text);
        var older = new ProxyRequest(proxy.Name, proxy.Transport, proxy.Host, proxy.Port, proxy.Path, true);
        var retained = older.Draft(proxy);
        Assert.Equal(18080, retained.ListenPort);
        Assert.False(retained.OriginHttp);
        var cleared = (older with { ListenPort = 0, OriginHttp = false }).Draft(proxy);
        Assert.Equal(0, cleared.ListenPort);
        Assert.False(cleared.OriginHttp);
    }

    [Fact]
    public async Task AnOldCatalogKeepsTlsBeforeAndAfterTheOriginMigration()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, new TransportProxyEntity
        {
            Name = "legacy", Host = "proxy.example.net", Port = 8443, Path = "relay", Opened = true,
        }, default).ConfigureAwait(false);
        var secret = proxy.Secret;
        await bench.Db.GetService<IMigrator>().MigrateAsync("TransportProxyOpening").ConfigureAwait(false);
        bench.Db.ChangeTracker.Clear();
        Assert.True(PanelStore.ServesOn(bench.DatabasePath, 8443));
        await bench.Db.Database.MigrateAsync().ConfigureAwait(false);
        var read = await store.FindAsync(proxy.Id, default).ConfigureAwait(false);
        Assert.Equal(secret, read!.Secret);
        Assert.Equal(0, read.ListenPort);
        Assert.False(read.OriginHttp);
        Assert.True(new ProxyRequest(read.Name, read.Transport, read.Host, read.Port, read.Path, true).Draft(read).Opened);
        Assert.True(PanelStore.ServesOn(bench.DatabasePath, 8443));
    }
}
