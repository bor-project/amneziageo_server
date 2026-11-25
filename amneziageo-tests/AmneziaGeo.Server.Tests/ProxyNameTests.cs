using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyNameTests
{
    [Theory]
    [InlineData("{HOST}-{TRANSPORT}-{PROXY}-{ID}-{PORT}-{PATH}", "cdn.example-http-relay-7-443-edge")]
    [InlineData("{ host }/{Proxy}/{transport}", "cdn.example/relay/http")]
    [InlineData("{PROXY}-{PATH}", "relay-edge")]
    [InlineData("office", "office")]
    [InlineData("---", "relay")]
    public void ATemplateUsesPublicProxyValues(string template, string expected)
    {
        var proxy = Proxy();
        var values = ProxyText.Values(proxy, "panel.example");
        Assert.Equal(expected, ProxyText.Title(proxy, template, "panel.example"));
        Assert.Equal(6, values.Count);
        Assert.Equal(ProxyName.Keys.Order(), values.Keys.Order());
        Assert.DoesNotContain(proxy.Secret, values.Values);
        Assert.DoesNotContain(proxy.CertificateKey, values.Values);
    }

    [Fact]
    public void AnEmptyLegacyHostUsesTheHelloHostAndAnEmptyValueDropsItsSeparator()
    {
        var proxy = Proxy();
        proxy.Host = "";
        proxy.Path = "";
        Assert.Equal("panel.example-relay", ProxyText.Title(proxy, "{HOST}-{PROXY}-{PATH}", "panel.example"));
        Assert.Empty(proxy.Host);
    }

    [Fact]
    public void TheOriginalNameTemplateKeepsItsSeparators()
    {
        var proxy = Proxy();
        proxy.Name = "_.relay-_";
        Assert.Equal(proxy.Name, ProxyText.Title(proxy, "{PROXY}"));
    }

    [Fact]
    public void LongPublishedNamesFitExistingClientsAndKeepSurrogatePairsWhole()
    {
        var proxy = Proxy();
        proxy.Host = new string('a', 100);
        Assert.Equal(new string('a', 64), ProxyText.Title(proxy, "{HOST}-{PROXY}"));
        var template = new string('a', 63) + "😀b";
        Assert.Equal(new string('a', 63), ProxyText.Title(proxy, template));
    }

    [Theory]
    [InlineData("{CLIENT}", "bad-proxy-name-template")]
    [InlineData("{}", "bad-proxy-name-template")]
    [InlineData("relay\n{PROXY}", "bad-proxy-name-template")]
    public void InvalidProxyTemplatesAreRefused(string template, string code)
    {
        Assert.Equal(code, PanelRules.Check(PanelDefaults.Settings with { ProxyNameTemplate = template })?.Code);
    }

    [Fact]
    public void ProxyAndClientSubstitutionsRemainSeparate()
    {
        Assert.Empty(ProxyName.Unknown("{host}-{Proxy}-{ Transport }-{ID}-{PORT}-{PATH}"));
        Assert.Equal(["{CLIENT}"], ProxyName.Unknown("{CLIENT}"));
        Assert.Equal(["{PROXY}"], ConfigName.Unknown("{PROXY}"));
        Assert.Equal("long-proxy-name-template", PanelRules.Check(PanelDefaults.Settings with
        {
            ProxyNameTemplate = new string('a', ProxyName.MaxLength + 1),
        })?.Code);
    }

    [Fact]
    public void AnOldPanelRequestPreservesTheProxyTemplateAndAnExplicitEmptyValueResetsIt()
    {
        var held = PanelDefaults.Settings with { ProxyNameTemplate = "{HOST}-{PROXY}" };
        var request = new PanelRequest(null, null, 8443, false, null, null, null, "en", false);
        Assert.Equal(held.ProxyNameTemplate, PanelAnswers.Draft(request, held).ProxyNameTemplate);
        Assert.Equal(ProxyName.Default, PanelAnswers.Draft(request).ProxyNameTemplate);
        Assert.Equal(ProxyName.Default, PanelAnswers.Draft(request with { ProxyNameTemplate = "  " }, held).ProxyNameTemplate);
        var draft = PanelAnswers.Draft(request with { ProxyNameTemplate = " {TRANSPORT}-{ID} " }, held);
        Assert.Equal("{TRANSPORT}-{ID}", draft.ProxyNameTemplate);
        Assert.False(draft.Differs(held));
        Assert.Equal(draft.ProxyNameTemplate, PanelAnswers.Panel(draft, held, new WebOptions(), false,
            new PanelPlace(8443, "/", false)).ProxyNameTemplate);
    }

    [Fact]
    public async Task AProxyTemplatePersistsAcrossDatabaseScopes()
    {
        using var bench = new Bench();
        var saved = await bench.Panel.SaveAsync(PanelDefaults.Settings with { ProxyNameTemplate = "{HOST}-{PROXY}" }, default).ConfigureAwait(false);
        Assert.True(saved.IsOk);
        using var scope = bench.Scopes.CreateScope();
        var reread = await scope.ServiceProvider.GetRequiredService<PanelStore>().ReadAsync(default).ConfigureAwait(false);
        Assert.Equal("{HOST}-{PROXY}", reread.ProxyNameTemplate);
    }

    [Theory]
    [InlineData(false, "websocket", "GET")]
    [InlineData(false, "xhttp", "GET")]
    [InlineData(true, "websocket", "GET")]
    [InlineData(true, "http", "GET")]
    [InlineData(true, "http", "POST")]
    [InlineData(true, "quic", "GET")]
    public async Task ATemplateChangeRenamesBothHelloCatalogsWithoutChangingConnectionMetadata(bool modern, string transport, string method)
    {
        using var bench = new Bench();
        var proxy = Proxy();
        proxy.Id = 0;
        proxy.Transport = transport;
        proxy.ProtocolVersion = proxy.IsHttp ? 2 : 0;
        proxy.HttpMethod = method;
        bench.Db.TransportProxies.Add(proxy);
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var template = (await bench.Templates.AddAsync(new ClientTemplate { Name = "selected", ProxyIds = [proxy.Id] }, default).ConfigureAwait(false)).Record!;
        var peer = new HelloPeer(new TunnelClient { Id = 7, Name = "phone" }, ConfigDefaults.Fresh("awg1"), template, new DefaultHttpContext());
        var offer = new ProxyOffer(bench.Scopes, modern);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var before = JsonSerializer.SerializeToElement(await offer.OfferAsync(peer, default).ConfigureAwait(false), options);
        var first = Assert.Single(before.GetProperty("items").EnumerateArray());
        Assert.Equal("relay-cdn.example-" + (proxy.IsHttp ? "http" : transport), first.GetProperty("name").GetString());
        await bench.Panel.SaveAsync(PanelDefaults.Settings with { ProxyNameTemplate = "{HOST}-{TRANSPORT}-{PROXY}" }, default).ConfigureAwait(false);
        var after = JsonSerializer.SerializeToElement(await offer.OfferAsync(peer, default).ConfigureAwait(false), options);
        var second = Assert.Single(after.GetProperty("items").EnumerateArray());
        Assert.Equal("cdn.example-" + (proxy.IsHttp ? "http" : transport) + "-relay", second.GetProperty("name").GetString());
        Assert.Equal(before.GetProperty("source").GetString(), after.GetProperty("source").GetString());
        foreach (var property in first.EnumerateObject().Where(property => property.Name != "name"))
        {
            Assert.Equal(property.Value.GetRawText(), second.GetProperty(property.Name).GetRawText());
        }

        Assert.Equal("relay", (await new TransportProxyStore(bench.Db).FindAsync(proxy.Id, default).ConfigureAwait(false))!.Name);
        Assert.Equal([proxy.Id], (await bench.Templates.FindAsync(template.Id, default).ConfigureAwait(false))!.ProxyIds);
    }

    [Fact]
    public async Task UpgradingAnExistingDatabaseKeepsProxyIdentityAndUsesTheNewDefault()
    {
        using var bench = new Bench();
        await bench.Panel.SaveAsync(PanelDefaults.Settings with { NameTemplate = "{CLIENT}-office" }, default).ConfigureAwait(false);
        var proxy = Proxy();
        proxy.Id = 0;
        bench.Db.TransportProxies.Add(proxy);
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var identity = await new TransportProxyStore(bench.Db).IdentityAsync(default).ConfigureAwait(false);
        await using var legacy = await LegacyDatabase.FromAsync(bench.Db, "LifetimeTraffic").ConfigureAwait(false);
        await legacy.MigrateAsync().ConfigureAwait(false);
        var settings = await new PanelStore(legacy.Db).ReadAsync(default).ConfigureAwait(false);
        var held = (await new TransportProxyStore(legacy.Db).FindAsync(proxy.Id, default).ConfigureAwait(false))!;
        var source = await new TransportProxyStore(legacy.Db).IdentityAsync(default).ConfigureAwait(false);
        Assert.Equal(ProxyName.Default, settings.ProxyNameTemplate);
        Assert.Equal("{CLIENT}-office", settings.NameTemplate);
        Assert.Equal("relay-cdn.example-http", ProxyText.Title(held, settings.ProxyNameTemplate));
        Assert.Equal(proxy.Secret, held.Secret);
        Assert.Equal(proxy.Host, held.Host);
        Assert.Equal(proxy.Path, held.Path);
        Assert.Equal(identity.Source, source.Source);
        Assert.Equal(identity.IdentityKey, source.IdentityKey);
    }

    [Theory]
    [InlineData("websocket", "relay-cdn.example-websocket")]
    [InlineData("http", "relay-cdn.example-http")]
    [InlineData("quic", "relay-cdn.example-quic")]
    public void TheDefaultIncludesTheNameHostAndTransport(string transport, string expected)
    {
        var proxy = Proxy();
        proxy.Transport = transport;
        Assert.Equal("{PROXY}-{HOST}-{TRANSPORT}", ProxyName.Default);
        Assert.Equal(expected, ProxyText.Title(proxy, ProxyName.Default));
    }

    [Theory]
    [InlineData("{PROXY}", "{PROXY}-{HOST}-{TRANSPORT}")]
    [InlineData("{TRANSPORT}:{PROXY}", "{TRANSPORT}:{PROXY}")]
    public async Task UpgradingTheDefaultPreservesCustomTemplates(string original, string expected)
    {
        using var bench = new Bench();
        await bench.Panel.SaveAsync(PanelDefaults.Settings with
        {
            ProxyNameTemplate = original, NameTemplate = "{CLIENT}-custom",
        }, default).ConfigureAwait(false);
        await using var legacy = await LegacyDatabase.FromAsync(bench.Db, "PendingInterfaceChanges").ConfigureAwait(false);
        await legacy.MigrateAsync().ConfigureAwait(false);
        var held = await new PanelStore(legacy.Db).ReadAsync(default).ConfigureAwait(false);
        Assert.Equal(expected, held.ProxyNameTemplate);
        Assert.Equal("{CLIENT}-custom", held.NameTemplate);
    }

    private static TransportProxyEntity Proxy() => new()
    {
        Id = 7, Name = "relay", Hosted = false, Host = "cdn.example", Port = 443, Path = "edge",
        Transport = "http", ProtocolVersion = 2, Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        CertificateKey = "/tls/private-key.pem", UpdatedUtc = DateTimeOffset.Parse("2026-11-22T12:00:00Z", CultureInfo.InvariantCulture),
    };
}
