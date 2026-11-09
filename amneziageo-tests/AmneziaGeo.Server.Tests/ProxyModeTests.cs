using System.Text.Json;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.XHttp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyModeTests
{
    [Theory]
    [InlineData("GET", false)]
    [InlineData("GET", true)]
    [InlineData("POST", false)]
    [InlineData("POST", true)]
    public async Task HttpModeAndCertificateSurviveSaving(string method, bool certificate)
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var draft = Draft("http");
        draft.HttpMethod = method;
        draft.UseCertificate = certificate;
        Assert.Null(ProxyRules.Check(draft));
        var saved = await store.SaveAsync(0, draft, default).ConfigureAwait(false);
        var credential = TransportProxyStore.Credential(saved, 42);
        var read = (await store.FindAsync(saved.Id, default).ConfigureAwait(false))!;
        Assert.Equal(method, read.HttpMethod);
        Assert.Equal(certificate, read.UsesCertificate);
        Assert.Equal(2, read.ProtocolVersion);
        Assert.True(await store.ServesAsync(18443, default).ConfigureAwait(false));
        read.Hosted = false;
        await store.SaveAsync(read.Id, read, default).ConfigureAwait(false);
        Assert.False(await store.ServesAsync(18443, default).ConfigureAwait(false));
        Assert.Equal(credential, TransportProxyStore.Credential((await store.FindAsync(read.Id, default).ConfigureAwait(false))!, 42));
        using var configuration = JsonDocument.Parse(XHttpFile.Render(new XHttpProxy(read.Id, 12345, read.Path, [credential], Method: method)));
        Assert.Equal(method, configuration.RootElement.GetProperty("httpGet").GetProperty("method").GetString());
    }

    [Fact]
    public void LegacyHttpOriginKeepsPublicTlsUntilExplicitlyChanged()
    {
        var legacy = Draft("xhttp");
        legacy.UseCertificate = null;
        legacy.OriginHttp = true;
        Assert.True(legacy.UsesCertificate);
        legacy.UseCertificate = false;
        Assert.False(legacy.UsesCertificate);
    }

    [Fact]
    public async Task OnlyAProvenPrototypeProfileIsAdopted()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone"), default).ConfigureAwait(false)).Record!;
        var store = new TransportProxyStore(bench.Db);
        var legacy = Draft("xhttp");
        legacy.ProtocolVersion = 0;
        legacy.UseCertificate = null;
        legacy.OriginHttp = true;
        var saved = await store.SaveAsync(0, legacy, default).ConfigureAwait(false);
        var credential = TransportProxyStore.Credential(saved, client.Id);
        var directory = Path.Combine(Path.GetTempPath(), "proxy-adoption-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "xhttp-" + saved.Id + ".json");
        try
        {
            await File.WriteAllTextAsync(file, "{\"inbounds\":[]}").ConfigureAwait(false);
            await store.AdoptHttpGetAsync(directory, default).ConfigureAwait(false);
            Assert.Equal(0, (await store.FindAsync(saved.Id, default).ConfigureAwait(false))!.ProtocolVersion);
            await File.WriteAllTextAsync(file, XHttpFile.Render(new XHttpProxy(saved.Id, 12345, "wrong", [credential]))).ConfigureAwait(false);
            await store.AdoptHttpGetAsync(directory, default).ConfigureAwait(false);
            Assert.Equal(0, (await store.FindAsync(saved.Id, default).ConfigureAwait(false))!.ProtocolVersion);
            await File.WriteAllTextAsync(file, XHttpFile.Render(new XHttpProxy(saved.Id, 12345, saved.Path, [credential]))).ConfigureAwait(false);
            await store.AdoptHttpGetAsync(directory, default).ConfigureAwait(false);
            var adopted = (await store.FindAsync(saved.Id, default).ConfigureAwait(false))!;
            Assert.Equal(2, adopted.ProtocolVersion);
            Assert.Equal(saved.Id, adopted.Id);
            Assert.Equal(saved.Secret, adopted.Secret);
            Assert.Equal(credential, TransportProxyStore.Credential(adopted, client.Id));
            Assert.Null(adopted.UseCertificate);
            Assert.True(adopted.OriginHttp);
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task MixedCatalogDoesNotPublishUnknownModesToOldClients()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default).ConfigureAwait(false)).Record!;
        var store = new TransportProxyStore(bench.Db);
        var websocket = await store.SaveAsync(0, Draft("websocket"), default).ConfigureAwait(false);
        var post = Draft("http");
        post.Name = "post";
        post.HttpMethod = "POST";
        var http = await store.SaveAsync(0, post, default).ConfigureAwait(false);
        var quic = await store.SaveAsync(0, Draft("quic"), default).ConfigureAwait(false);
        var template = (await bench.Templates.AddAsync(new ClientTemplate { Name = "selected", ProxyIds = [websocket.Id, http.Id, quic.Id] }, default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone") with { TemplateId = template.Id }, default).ConfigureAwait(false)).Record!;
        var peer = new HelloPeer(client, endpoint, template, new DefaultHttpContext());
        var scopes = bench.Services.GetRequiredService<IServiceScopeFactory>();
        var old = Assert.IsType<ProxiesFeature>(await new ProxyOffer(scopes).OfferAsync(peer, default).ConfigureAwait(false));
        Assert.Equal("websocket", Assert.Single(old.Items).Transport);
        var modern = Assert.IsType<ProxiesFeatureV2>(await new ProxyOffer(scopes, true).OfferAsync(peer, default).ConfigureAwait(false));
        Assert.Equal(3, modern.Items.Count);
        Assert.Equal("POST", modern.Items.Single(one => one.Transport == "http").Http!.Method);
        Assert.Equal("webtransport", modern.Items.Single(one => one.Transport == "quic").Quic!.Protocol);
    }

    [Fact]
    public async Task PlainGetUsesALegacyCompatibleSchemeWithoutATlsPin()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default).ConfigureAwait(false)).Record!;
        var draft = Draft("http");
        draft.UseCertificate = false;
        draft.Pin = new string('a', 64);
        var proxy = await new TransportProxyStore(bench.Db).SaveAsync(0, draft, default).ConfigureAwait(false);
        var peer = new HelloPeer(new TunnelClient { Id = 7, Name = "phone" }, endpoint,
            new ClientTemplate { Name = "selected", ProxyIds = [proxy.Id] }, new DefaultHttpContext());
        var feature = Assert.IsType<ProxiesFeature>(await new ProxyOffer(bench.Scopes).OfferAsync(peer, default).ConfigureAwait(false));
        var item = Assert.Single(feature.Items);
        Assert.Equal("xhttp", item.Transport);
        Assert.Equal("http", item.Xhttp!.Scheme);
        Assert.Equal(2, item.Xhttp.Version);
        Assert.Empty(item.Tls.Pin);
    }

    [Fact]
    public void InvalidStandaloneCatalogsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => ProxyCatalog.Check(new ProxyCatalog(1, null!)));
        Assert.Throws<InvalidDataException>(() => ProxyCatalog.Check(new ProxyCatalog(1, [null!])));
        var entry = new ProxyServiceEntry(1, "get", "http", "proxy.example.net", 80, "relay", ["credential"], false);
        Assert.Throws<InvalidDataException>(() => ProxyCatalog.Check(new ProxyCatalog(1, [entry])));
        Assert.Throws<InvalidDataException>(() => ProxyCatalog.Check(new ProxyCatalog(1, [entry with { Credentials = null! }])));
    }

    [Fact]
    public async Task InvalidCatalogKeepsWatchingAndRecovers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "proxy-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "proxies.json");
        var status = Path.Combine(directory, "state", "status.json");
        using var stop = new CancellationTokenSource();
        await using var service = new StandaloneProxyService(Path.Combine(directory, "state"), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await File.WriteAllTextAsync(file, "{\"version\":1,\"proxies\":null}").ConfigureAwait(false);
        var watching = service.WatchAsync(file, stop.Token);
        try
        {
            await WaitForStatusAsync(true).ConfigureAwait(false);
            Assert.False(watching.IsCompleted);
            await File.WriteAllTextAsync(file, "{\"version\":1,\"proxies\":[]}").ConfigureAwait(false);
            await WaitForStatusAsync(false).ConfigureAwait(false);
            Assert.False(watching.IsCompleted);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await watching.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
            }

            Directory.Delete(directory, true);
        }

        async Task WaitForStatusAsync(bool fault)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (File.Exists(status))
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(status).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("fault", out var current) == fault)
                    {
                        if (fault)
                        {
                            Assert.Equal("invalid-proxy-catalog", current.GetString());
                        }

                        Assert.Equal(0, document.RootElement.GetProperty("proxies").GetArrayLength());
                        return;
                    }
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            Assert.Fail("The proxy catalog status did not change.");
        }
    }

    [Fact]
    public void QuicRulesBindCredentialsAndUdpTargets()
    {
        var entry = new ProxyServiceEntry(1, "relay", "quic", "proxy.example.net", 443, "relay", ["credential"],
            Targets: new ProxyTargets(true, [new ProxyDestination("vpn.example.net", 51820)]));
        var rules = QuicHost.Rules([entry]);
        Assert.Contains("!PathPrefix '^relay$'", rules);
        Assert.Contains("!Authorization '^Bearer (?:credential)$'", rules);
        Assert.Contains("          - Udp", rules);
        Assert.Contains("          - 51820", rules);
        Assert.DoesNotContain("0.0.0.0/0", rules);
        var plain = Draft("quic");
        plain.UseCertificate = false;
        Assert.NotNull(ProxyRules.Check(plain));
    }

    [Fact]
    public void LegacyRequestKeepsModeWhileExplicitHttpConvertsIt()
    {
        var legacy = Draft("xhttp");
        legacy.ProtocolVersion = 0;
        var old = new ProxyRequest("old", "xhttp", legacy.Host, legacy.Port, legacy.Path, true).Draft(legacy);
        Assert.Equal(0, old.ProtocolVersion);
        Assert.NotNull(ProxyRules.Check(old));
        var converted = new ProxyRequest("new", "http", legacy.Host, legacy.Port, legacy.Path, true, UseCertificate: false, HttpMethod: "GET").Draft(legacy);
        Assert.Equal(2, converted.ProtocolVersion);
        Assert.Null(ProxyRules.Check(converted));
    }

    private static TransportProxyEntity Draft(string transport) => new()
    {
        Name = transport, Transport = transport, Host = "proxy.example.net", Port = 18443, Path = transport,
        UseCertificate = true, ProtocolVersion = transport == "http" ? 2 : 0,
    };
}
