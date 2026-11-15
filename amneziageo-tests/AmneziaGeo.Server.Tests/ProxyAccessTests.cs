using System.Text.Json;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Carrier;
using AmneziaGeo.Server.Routing.Proxy;
using AmneziaGeo.Server.Routing.XHttp;
using Microsoft.AspNetCore.Http;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyAccessTests
{
    [Fact]
    public async Task TokensHaveIndependentSecretsAndMetadataAndSurviveOldUpdates()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, Draft(), default).ConfigureAwait(false);
        var derived = TransportProxyStore.Credential(proxy, 7);
        var first = (await store.MintAsync(proxy.Id, "phone", default).ConfigureAwait(false)).Minted!;
        var second = (await store.MintAsync(proxy.Id, "server", default).ConfigureAwait(false)).Minted!;
        Assert.NotEqual(first.Secret, second.Secret);
        Assert.NotEqual(first.Token.Id, first.Secret);
        Assert.True(Guid.TryParseExact(first.Secret, "D", out _));
        var held = (await store.FindAsync(proxy.Id, default).ConfigureAwait(false))!;
        Assert.DoesNotContain(first.Secret, held.TokensJson);
        Assert.DoesNotContain(second.Secret, held.TokensJson);
        var metadata = JsonSerializer.Serialize(store.Tokens(held));
        Assert.DoesNotContain(first.Secret, metadata);
        Assert.DoesNotContain("Encrypted", metadata);
        Assert.Equal(2, store.Tokens(held).Count);
        var credentials = await store.CredentialsAsync(held, default).ConfigureAwait(false);
        Assert.Contains(first.Secret, credentials);
        Assert.Contains(second.Secret, credentials);

        var request = new ProxyRequest("renamed", "websocket", "proxy.example", 443, "relay", true);
        await store.SaveAsync(proxy.Id, request.Draft(held), default).ConfigureAwait(false);
        var renamed = (await store.FindAsync(proxy.Id, default).ConfigureAwait(false))!;
        Assert.Equal(held.TokensJson, renamed.TokensJson);
        Assert.Contains(first.Secret, await store.CredentialsAsync(renamed, default).ConfigureAwait(false));
        Assert.Equal(derived, TransportProxyStore.Credential(renamed, 7));
        Assert.Equal(string.Empty, store.AccessRevision(renamed));

        var previous = store.AccessRevision(renamed);
        Assert.True(await store.RevokeAsync(proxy.Id, first.Token.Id, default).ConfigureAwait(false));
        var revoked = (await store.FindAsync(proxy.Id, default).ConfigureAwait(false))!;
        Assert.DoesNotContain(first.Secret, await store.CredentialsAsync(revoked, default).ConfigureAwait(false));
        Assert.Contains(second.Secret, await store.CredentialsAsync(revoked, default).ConfigureAwait(false));
        Assert.NotEqual(previous, store.AccessRevision(revoked));
        Assert.False(await store.RevokeAsync(proxy.Id, first.Token.Id, default).ConfigureAwait(false));
    }

    [Fact]
    public async Task TokensCannotBeCopiedBetweenProxiesOrRevokedThroughAnotherProxy()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var first = await store.SaveAsync(0, Draft(), default).ConfigureAwait(false);
        var another = Draft();
        another.Name = "another";
        var second = await store.SaveAsync(0, another, default).ConfigureAwait(false);
        var minted = (await store.MintAsync(first.Id, "phone", default).ConfigureAwait(false)).Minted!;
        first = (await store.FindAsync(first.Id, default).ConfigureAwait(false))!;
        Assert.False(await store.RevokeAsync(second.Id, minted.Token.Id, default).ConfigureAwait(false));
        second.TokensJson = first.TokensJson;
        Assert.Empty(await store.CredentialsAsync(second, default).ConfigureAwait(false));
        second.TokensJson = "not-json";
        Assert.Empty(await store.CredentialsAsync(second, default).ConfigureAwait(false));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\nname")]
    public async Task InvalidNamesDoNotCreateTokens(string name)
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, Draft(), default).ConfigureAwait(false);
        var result = await store.MintAsync(proxy.Id, name, default).ConfigureAwait(false);
        Assert.Null(result.Minted);
        Assert.Equal("bad-proxy-token-name", result.Error);
        Assert.Empty(store.Tokens((await store.FindAsync(proxy.Id, default).ConfigureAwait(false))!));
    }

    [Fact]
    public async Task DuplicateNamesAndTokenLimitAreEnforced()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, Draft(), default).ConfigureAwait(false);
        for (var index = 0; index < 64; index++)
        {
            Assert.NotNull((await store.MintAsync(proxy.Id, "token" + index, default).ConfigureAwait(false)).Minted);
            if (index == 0)
            {
                Assert.Equal("proxy-token-name-taken", (await store.MintAsync(proxy.Id, "TOKEN0", default).ConfigureAwait(false)).Error);
            }
        }
        Assert.Equal("proxy-token-limit", (await store.MintAsync(proxy.Id, "overflow", default).ConfigureAwait(false)).Error);
    }

    [Fact]
    public void HttpManualTokensRequireBearerAndKeepLegacySignedAuthentication()
    {
        var manual = Guid.NewGuid().ToString("D");
        var legacy = Guid.NewGuid().ToString("D");
        var identity = ProxyAccess.HttpIdentity(manual);
        var legacyIdentity = ProxyAccess.HttpIdentity(legacy);
        Assert.False(ProxyAccess.AllowsHttp(false, [identity], "", identity));
        Assert.True(ProxyAccess.AllowsHttp(false, [identity], "Bearer " + manual, identity));
        Assert.False(ProxyAccess.AllowsHttp(false, [identity], "Bearer " + legacy, identity));
        Assert.False(ProxyAccess.AllowsHttp(false, [identity], "Basic " + manual, identity));
        Assert.False(ProxyAccess.AllowsHttp(false, [identity], "Bearer " + manual + "\r\n", identity));
        Assert.True(ProxyAccess.AllowsHttp(false, [identity], "", legacyIdentity));
        Assert.True(ProxyAccess.AllowsHttp(false, [identity], "Bearer " + legacy, legacyIdentity));
        Assert.True(ProxyAccess.AllowsHttp(true, [identity], "", identity));
    }

    [Fact]
    public void AnonymousHttpRetainsVersionTwoAndAnUnknownBearerIdentityIsRejected()
    {
        using var rendered = JsonDocument.Parse(XHttpFile.Render(new XHttpProxy(1, 18443, "relay", [], AllowAnonymous: true)));
        var settings = rendered.RootElement.GetProperty("httpGet");
        Assert.Equal(2, settings.GetProperty("version").GetInt32());
        Assert.True(settings.GetProperty("allowAnonymous").GetBoolean());
        Assert.Equal(ProxyAccess.AnonymousCredential, Assert.Single(settings.GetProperty("credentials").EnumerateArray()).GetString());
        Assert.Throws<ArgumentException>(() => XHttpFile.Render(new XHttpProxy(1, 18443, "relay", [Guid.NewGuid().ToString("D")], BearerIds: [new string('0', 64)])));
    }

    [Fact]
    public void AnonymousQuicKeepsTheDestinationRestriction()
    {
        var proxy = new ProxyServiceEntry(1, "relay", "quic", "proxy.example", 443, "relay", [Guid.NewGuid().ToString("D")],
            Targets: new ProxyTargets(true, [new("vpn.example", 45120)]));
        var authenticated = QuicHost.Rules([proxy]);
        Assert.Contains("!Authorization", authenticated);
        var anonymous = QuicHost.Rules([proxy with { AllowAnonymous = true, Credentials = [] }]);
        Assert.DoesNotContain("!Authorization", anonymous);
        Assert.DoesNotContain("0.0.0.0/0", anonymous);
        Assert.Contains("45120", anonymous);
        Assert.Contains("vpn", anonymous);
    }

    [Theory]
    [InlineData("websocket", "GET")]
    [InlineData("http", "GET")]
    [InlineData("http", "POST")]
    [InlineData("quic", "GET")]
    public async Task AnonymousHelloPublishesTheFlagAndPreservesScope(string transport, string method)
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var draft = Draft();
        draft.Transport = transport;
        draft.ProtocolVersion = transport == "http" ? 2 : 0;
        draft.HttpMethod = method;
        draft.AllowAnonymous = true;
        draft.TargetsJson = ProxyTargetRules.Write(new ProxyTargets(true, [new("vpn.example", 45120)]));
        var proxy = await store.SaveAsync(0, draft, default).ConfigureAwait(false);
        var peer = new HelloPeer(new TunnelClient { Id = 7, Name = "phone" }, ConfigDefaults.Fresh("local"),
            new ClientTemplate { Name = "selected", ProxyIds = [proxy.Id] }, new DefaultHttpContext());
        var modern = Assert.IsType<ProxiesFeatureV2>(await new ProxyOffer(bench.Scopes, true).OfferAsync(peer, default).ConfigureAwait(false));
        var item = Assert.Single(modern.Items);
        Assert.True(item.Anonymous);
        Assert.Equal("Authorization", item.Auth.Header);
        Assert.Equal(ProxyAccess.AnonymousCredential, item.Auth.Credential);
        Assert.Equal("selected", item.Scope);
        Assert.False(ProxyTargetRules.Allows(item.Targets, "other.example", 45120));
    }

    [Fact]
    public void WsHandshakePlacesTheAccessTokenOnlyInAuthorization()
    {
        var token = Guid.NewGuid().ToString("D");
        var handshake = WsCarrier.Handshake(new WsEndpoint("proxy.example", 443, "relay", ""), 45120, "key", "Bearer " + token, "vpn.example");
        Assert.Single(handshake.Split("\r\n").Where(line => line.Contains(token, StringComparison.Ordinal)));
        Assert.Contains("Authorization: Bearer " + token + "\r\n", handshake);
        Assert.DoesNotContain(token, handshake.Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Protocol:", StringComparison.Ordinal)));
    }

    private static TransportProxyEntity Draft() => new()
    {
        Hosted = false, Name = "relay", Host = "proxy.example", Port = 443, Path = "relay",
    };
}
