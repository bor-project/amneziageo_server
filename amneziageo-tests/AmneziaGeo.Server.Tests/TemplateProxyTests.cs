using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class TemplateProxyTests
{
    [Fact]
    public async Task TemplatesRoundTripZeroOneAndManyProxiesWithoutDuplicates()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var first = await store.SaveAsync(0, Proxy("first"), default).ConfigureAwait(false);
        var second = await store.SaveAsync(0, Proxy("second"), default).ConfigureAwait(false);
        var created = await bench.Templates.AddAsync(new ClientTemplate { Name = "selection" }, default).ConfigureAwait(false);
        Assert.True(created.IsOk);
        var template = created.Record!;
        foreach (var ids in new long[][] { [first.Id], [second.Id, first.Id, second.Id], [] })
        {
            var changed = await bench.Templates.ChangeAsync(template.Id, template with { ProxyIds = ids }, default).ConfigureAwait(false);
            Assert.True(changed.IsOk);
            template = (await bench.Templates.FindAsync(template.Id, default).ConfigureAwait(false))!;
            Assert.Equal(ids.Distinct(), template.ProxyIds);
            Assert.Equal(template.ProxyIds, TemplateAnswers.Template(template, []).ProxyIds);
        }

        var refusal = await bench.Templates.AddAsync(new ClientTemplate { Name = "unknown", ProxyIds = [first.Id, -1] }, default).ConfigureAwait(false);
        Assert.Equal("unknown-proxy", refusal.Code);
        Assert.False(refusal.IsOk);
    }

    [Fact]
    public async Task CurrentTemplateAndProxyChangesAppearInTheNextOffer()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var first = await store.SaveAsync(0, Proxy("first"), default).ConfigureAwait(false);
        var second = await store.SaveAsync(0, Proxy("second"), default).ConfigureAwait(false);
        var one = (await bench.Templates.AddAsync(new ClientTemplate { Name = "one", ProxyIds = [first.Id] }, default).ConfigureAwait(false)).Record!;
        var two = (await bench.Templates.AddAsync(new ClientTemplate { Name = "two", ProxyIds = [second.Id] }, default).ConfigureAwait(false)).Record!;
        var offer = new ProxyOffer(bench.Scopes);
        var endpoint = ConfigDefaults.Fresh("vpn");
        async Task<ProxiesFeature> Read(long id, long client)
        {
            var current = await bench.Templates.FindAsync(id, default).ConfigureAwait(false);
            return Assert.IsType<ProxiesFeature>(await offer.OfferAsync(new HelloPeer(
                new TunnelClient { Id = client, Name = "client" }, endpoint, current, new DefaultHttpContext()), default).ConfigureAwait(false));
        }

        Assert.Equal(first.Id.ToString(), Assert.Single((await Read(one.Id, 7).ConfigureAwait(false)).Items).Id);
        Assert.Equal(second.Id.ToString(), Assert.Single((await Read(two.Id, 8).ConfigureAwait(false)).Items).Id);
        first.Host = "cdn.example.net";
        first.Port = 9443;
        first.Path = "changed";
        await store.SaveAsync(first.Id, first, default).ConfigureAwait(false);
        var edited = Assert.Single((await Read(one.Id, 7).ConfigureAwait(false)).Items);
        Assert.Equal(("cdn.example.net", 9443, "changed"), (edited.Host, edited.Port, edited.Path));
        await bench.Templates.ChangeAsync(one.Id, one with { ProxyIds = [second.Id, first.Id] }, default).ConfigureAwait(false);
        Assert.Equal([second.Id.ToString(), first.Id.ToString()], (await Read(one.Id, 7).ConfigureAwait(false)).Items.Select(item => item.Id));
        second.IsEnabled = false;
        await store.SaveAsync(second.Id, second, default).ConfigureAwait(false);
        Assert.Single((await Read(one.Id, 7).ConfigureAwait(false)).Items);
        Assert.Empty((await Read(two.Id, 8).ConfigureAwait(false)).Items);
        Assert.Equal([second.Id], (await bench.Templates.FindAsync(two.Id, default).ConfigureAwait(false))!.ProxyIds);
        await store.RemoveAsync(first.Id, default).ConfigureAwait(false);
        Assert.Equal([second.Id], (await bench.Templates.FindAsync(one.Id, default).ConfigureAwait(false))!.ProxyIds);
        Assert.Empty((await Read(one.Id, 7).ConfigureAwait(false)).Items);
    }

    private static TransportProxyEntity Proxy(string name) => new()
    {
        Name = name, Host = "proxy.example.net", Port = 8443, Path = name,
    };
}
