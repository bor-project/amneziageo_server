using System.Text;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyTargetTests
{
    [Theory]
    [InlineData(" Example.COM. ", "example.com")]
    [InlineData("BÜCHER.example", "xn--bcher-kva.example")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("[2001:0DB8:0:0::1]", "2001:db8::1")]
    [InlineData("::ffff:192.0.2.1", "192.0.2.1")]
    public void HostsUseCanonicalExactIdentities(string input, string expected)
    {
        Assert.True(ProxyTargetRules.TryHost(input, out var host));
        Assert.Equal(expected, host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("foo..")]
    [InlineData("[foo]")]
    [InlineData("*.example")]
    [InlineData("example/24")]
    [InlineData("https://example.com")]
    [InlineData("user@example.com")]
    [InlineData("example.com:443")]
    [InlineData("fe80::1%eth0")]
    [InlineData("bad..example")]
    [InlineData("-bad.example")]
    [InlineData("bad_name")]
    public void InvalidHostsNeverBecomeWildcardPolicies(string input)
    {
        Assert.False(ProxyTargetRules.TryHost(input, out _));
    }

    [Fact]
    public void CanonicalPoliciesDeduplicateSortAndKeepHostPortPairs()
    {
        var draft = new ProxyTargets(true, [
            new("EXAMPLE.COM.", 443), new("example.com", 443), new("example.com", 51820),
            new("127.0.0.1", 51821),
        ]);
        Assert.True(ProxyTargetRules.TryNormalize(draft, out var policy));
        Assert.Equal(3, policy!.Destinations.Count);
        Assert.Equal("127.0.0.1", policy.Destinations[0].Host);
        Assert.True(ProxyTargetRules.Allows(policy, "example.COM.", 443));
        Assert.True(ProxyTargetRules.Allows(policy, "127.0.0.1", 51821));
        Assert.False(ProxyTargetRules.Allows(policy, "127.0.0.1", 443));
        Assert.False(ProxyTargetRules.Allows(policy, "other.example", 443));
        Assert.False(ProxyTargetRules.Allows(policy, "example.com", 51821));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingDestinationCollectionsAreInvalid(bool restricted)
    {
        Assert.False(ProxyTargetRules.TryNormalize(new ProxyTargets(restricted, null!), out _));
    }

    [Fact]
    public void UnrestrictedCanRetainInactiveDestinationsAndEmptyRestrictedCannot()
    {
        var inactive = new ProxyTargets(false, [new("example.com", 443)]);
        Assert.True(ProxyTargetRules.Allows(inactive, "other.example", 51820));
        Assert.False(ProxyTargetRules.TryNormalize(new ProxyTargets(true, []), out _));
        Assert.True(ProxyTargetRules.TryNormalize(new ProxyTargets(false, []), out _));
        Assert.False(ProxyTargetRules.Allows(null, "example.com", 0));
    }

    [Fact]
    public void DistinctLimitAndRawInputLimitApplyBeforeRuntimeRendering()
    {
        Assert.True(ProxyTargetRules.TryNormalize(new ProxyTargets(true,
            [.. Enumerable.Repeat(new ProxyDestination("example.com", 443), 1024)]), out var deduplicated));
        Assert.Single(deduplicated!.Destinations);
        Assert.False(ProxyTargetRules.TryNormalize(new ProxyTargets(true,
            [.. Enumerable.Repeat(new ProxyDestination("example.com", 443), 1025)]), out _));
        Assert.False(ProxyTargetRules.TryNormalize(new ProxyTargets(true,
            [.. Enumerable.Range(1, 65).Select(port => new ProxyDestination("example.com", port))]), out _));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"restricted\":false,\"destinations\":null}")]
    [InlineData("{\"restricted\":true,\"destinations\":[]}")]
    [InlineData("{\"restricted\":false,\"destinations\":[{\"host\":\"*\",\"port\":443}]}")]
    public void CorruptPersistenceDeniesEveryDestination(string text)
    {
        var policy = ProxyTargetRules.Read(text);
        Assert.NotNull(policy);
        Assert.True(policy.Restricted);
        Assert.False(ProxyTargetRules.Allows(policy, "127.0.0.1", 51820));
    }

    [Fact]
    public void ExistingEmptyPersistenceKeepsUnrestrictedBehavior()
    {
        Assert.Null(ProxyTargetRules.Read(null));
        Assert.Null(ProxyTargetRules.Read(string.Empty));
        Assert.True(ProxyTargetRules.Allows(ProxyTargetRules.Read(string.Empty), "vpn.example", 443));
    }

    [Fact]
    public void RealWebSocketTokenNamesTheTargetIndependentlyOfSupplementalHeader()
    {
        var handshake = WsCarrier.Handshake(new WsEndpoint("proxy.example", 443, "relay", ""), 51820, "key", "Bearer token", "denied.example");
        var protocols = handshake.Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Protocol:", StringComparison.Ordinal))[23..].Trim();
        Assert.True(ProxyWsTarget.TryRead(protocols, out var target));
        Assert.Equal(new ProxyDestination("denied.example", 51820), target);
        var policy = new ProxyTargets(true, [new("allowed.example", 51820)]);
        Assert.False(ProxyTargetRules.Allows(policy, target!.Host, target.Port));
        Assert.False(ProxyWsTarget.TryRead(protocols + ", " + protocols.Split(", ")[1], out _));
    }

    [Fact]
    public void ATokenCannotHideTcpBehindAnAllowedUdpTarget()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"r\":\"allowed.example\",\"rp\":443,\"p\":{\"Tcp\":{}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.False(ProxyWsTarget.TryRead("v1, authorization.bearer.e30." + payload + ".e30", out _));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"443\"")]
    [InlineData("{}")]
    [InlineData("true")]
    [InlineData("2147483648")]
    [InlineData("443.1")]
    public void MalformedTokenPortsAreRejected(string port)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"r\":\"allowed.example\",\"rp\":" + port + ",\"p\":{\"Udp\":{}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.False(ProxyWsTarget.TryRead("v1, authorization.bearer.e30." + payload + ".e30", out _));
    }

    [Fact]
    public void BackendRulesKeepUnrestrictedAndLegacyBehaviorWhileRestrictedDeniesByDefault()
    {
        var targets = new ProxyTargets(true, [new("127.0.0.1", 51820), new("vpn.example", 443)]);
        var rules = ProxyFile.Whitelist(0, "relay", targets);
        Assert.Contains("127.0.0.1/32", rules);
        Assert.Contains("cidr: []", rules);
        Assert.DoesNotContain("0.0.0.0/0", rules);
        Assert.Equal(ProxyFile.Whitelist(51820, "legacy"), ProxyFile.Whitelist(51820, "legacy", targets));
        Assert.Contains("allow: []", ProxyFile.Whitelist(0, "relay", ProxyTargetRules.Denied));
        var user = Guid.NewGuid().ToString("D");
        var unlimited = XHttpFile.Render(new XHttpProxy(1, 18080, "relay", [user]));
        Assert.Equal(unlimited, XHttpFile.Render(new XHttpProxy(1, 18080, "relay", [user], new ProxyTargets(false, [new("vpn.example", 443)]))));
        using var document = JsonDocument.Parse(XHttpFile.Render(new XHttpProxy(1, 18080, "relay", [user], targets)));
        var routing = document.RootElement.GetProperty("routing");
        Assert.Equal("AsIs", routing.GetProperty("domainStrategy").GetString());
        var policies = routing.GetProperty("rules");
        Assert.Equal(3, policies.GetArrayLength());
        Assert.Equal("target-denied", policies[2].GetProperty("outboundTag").GetString());
        Assert.Equal("blackhole", document.RootElement.GetProperty("outbounds")[1].GetProperty("protocol").GetString());
        using var denied = JsonDocument.Parse(XHttpFile.Render(new XHttpProxy(1, 18080, "relay", [user], ProxyTargetRules.Denied)));
        Assert.Single(denied.RootElement.GetProperty("routing").GetProperty("rules").EnumerateArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"targets\":null")]
    public async Task OldOrNullRequestPreservesHeldPolicyAndCredential(string extra)
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var entity = await store.SaveAsync(0, new TransportProxyEntity
        {
            Name = "relay", Host = "proxy.example", Port = 443, Path = "relay",
            TargetsJson = ProxyTargetRules.Write(new ProxyTargets(true, [new("vpn.example", 51820)])),
        }, default).ConfigureAwait(false);
        var credential = TransportProxyStore.Credential(entity, 7);
        var body = "{\"name\":\"renamed\",\"transport\":\"websocket\",\"host\":\"proxy.example\",\"port\":443,\"path\":\"relay\",\"isEnabled\":true" + extra + "}";
        var request = JsonSerializer.Deserialize<ProxyRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var saved = await store.SaveAsync(entity.Id, request.Draft(entity), default).ConfigureAwait(false);
        Assert.True(ProxyTargetRules.Read(saved.TargetsJson)!.Restricted);
        Assert.Equal(credential, TransportProxyStore.Credential(saved, 7));
        var released = request with { Targets = new ProxyTargets(false, [new("vpn.example", 51820)]) };
        var disabled = await store.SaveAsync(saved.Id, released.Draft(saved), default).ConfigureAwait(false);
        Assert.False(ProxyTargetRules.Read(disabled.TargetsJson)!.Restricted);
        Assert.Equal(credential, TransportProxyStore.Credential(disabled, 7));
    }

    [Fact]
    public async Task HelloPublishesRestrictedPolicyWithoutChangingItsVersionOrIdentity()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, new TransportProxyEntity
        {
            Name = "relay", Host = "proxy.example", Port = 443, Path = "relay",
            TargetsJson = ProxyTargetRules.Write(new ProxyTargets(true, [new("vpn.example", 51820)])),
        }, default).ConfigureAwait(false);
        var peer = new HelloPeer(new TunnelClient { Id = 7, Name = "peer" }, ConfigDefaults.Fresh("vpn"),
            new ClientTemplate { Name = "template", ProxyIds = [proxy.Id] }, new DefaultHttpContext());
        var offer = new ProxyOffer(bench.Scopes);
        var before = Assert.IsType<ProxiesFeature>(await offer.OfferAsync(peer, default).ConfigureAwait(false));
        Assert.Equal(1, before.Version);
        Assert.True(Assert.Single(before.Items).Targets!.Restricted);
        proxy.TargetsJson = string.Empty;
        await store.SaveAsync(proxy.Id, proxy, default).ConfigureAwait(false);
        var after = Assert.IsType<ProxiesFeature>(await offer.OfferAsync(peer, default).ConfigureAwait(false));
        Assert.Null(Assert.Single(after.Items).Targets);
        Assert.Equal(before.Source, after.Source);
        Assert.Equal(before.Items[0].Auth, after.Items[0].Auth);
    }

    [Fact]
    public async Task MigrationLeavesExistingCatalogUnrestrictedAndKeepsItsIdentity()
    {
        using var bench = new Bench();
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, new TransportProxyEntity { Name = "old", Host = "vpn.example", Port = 443, Path = "old" }, default).ConfigureAwait(false);
        var secret = proxy.Secret;
        await bench.Db.GetService<IMigrator>().MigrateAsync("PrivateEndpointUdp").ConfigureAwait(false);
        bench.Db.ChangeTracker.Clear();
        await bench.Db.Database.MigrateAsync().ConfigureAwait(false);
        var restored = await store.FindAsync(proxy.Id, default).ConfigureAwait(false);
        Assert.Equal(secret, restored!.Secret);
        Assert.Equal("vpn.example", restored.Host);
        Assert.Null(ProxyTargetRules.Read(restored.TargetsJson));
    }

    [Theory]
    [InlineData("xhttp")]
    [InlineData("websocket")]
    public async Task TargetedProbeDistinguishesPermissionFromGeneralAvailability(string transport)
    {
        var policy = new ProxyTargets(true, [new("vpn.example", 51820)]);
        async Task<(int Status, JsonDocument Body)> Check(string query)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.QueryString = new QueryString(query);
            using var output = new MemoryStream();
            context.Response.Body = output;
            await XHttpProbe.AnswerAsync(context, new string('a', 32), () => Task.FromResult(true), true, () => policy, transport).ConfigureAwait(false);
            return (context.Response.StatusCode, JsonDocument.Parse(output.ToArray()));
        }

        var general = await Check(string.Empty).ConfigureAwait(false);
        using (general.Body)
        {
            Assert.Equal(200, general.Status);
            Assert.False(general.Body.RootElement.TryGetProperty("target", out _));
        }

        var accepted = await Check("?host=VPN.EXAMPLE.&port=51820").ConfigureAwait(false);
        using (accepted.Body)
        {
            Assert.Equal(200, accepted.Status);
            Assert.True(accepted.Body.RootElement.GetProperty("allowed").GetBoolean());
            Assert.Equal("vpn.example", accepted.Body.RootElement.GetProperty("target").GetProperty("host").GetString());
        }

        var refused = await Check("?host=other.example&port=51820").ConfigureAwait(false);
        using (refused.Body)
        {
            Assert.Equal(403, refused.Status);
            Assert.Equal("proxy-target-denied", refused.Body.RootElement.GetProperty("error").GetString());
        }

        var malformed = await Check("?host=vpn.example").ConfigureAwait(false);
        using (malformed.Body)
        {
            Assert.Equal(400, malformed.Status);
            Assert.Equal("bad-proxy-target", malformed.Body.RootElement.GetProperty("error").GetString());
        }
    }
}
