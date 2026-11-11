using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxySourceProofTests
{
    [Fact]
    public async Task InterfacesAndClientsShareOneCatalogSigningKey()
    {
        using var bench = new Bench();
        var proxy = await new TransportProxyStore(bench.Db).SaveAsync(0, new TransportProxyEntity
        {
            Hosted = false,
            Name = "shared", Host = "proxy.example.net", Port = 443, Path = "relay",
        }, default).ConfigureAwait(false);
        var template = new ClientTemplate { Name = "selected", ProxyIds = [proxy.Id] };
        var firstEndpoint = ConfigDefaults.Fresh("one");
        var secondEndpoint = ConfigDefaults.Fresh("two");
        var first = await OfferAsync(bench, firstEndpoint, 7, template).ConfigureAwait(false);
        var second = await OfferAsync(bench, secondEndpoint, 8, template).ConfigureAwait(false);
        var reread = await OfferAsync(bench, firstEndpoint, 9, template).ConfigureAwait(false);
        var firstProof = Assert.IsType<ProxySourceProof>(first.Proof);
        var secondProof = Assert.IsType<ProxySourceProof>(second.Proof);
        var rereadProof = Assert.IsType<ProxySourceProof>(reread.Proof);

        Assert.Equal(first.Source, second.Source);
        Assert.Equal(first.Source, reread.Source);
        Assert.Equal(firstProof.PublicKey, secondProof.PublicKey);
        Assert.Equal(firstProof.PublicKey, rereadProof.PublicKey);
        Assert.Equal(firstEndpoint.PublicKey, firstProof.Issuer);
        Assert.Equal(secondEndpoint.PublicKey, secondProof.Issuer);
        Assert.NotEqual(firstProof.Issuer, secondProof.Issuer);
        Assert.True(Verify(firstProof, first.Source, firstEndpoint.PublicKey));
        Assert.True(Verify(secondProof, second.Source, secondEndpoint.PublicKey));
        Assert.True(Verify(rereadProof, reread.Source, firstEndpoint.PublicKey));
        Assert.NotEqual(Assert.Single(first.Items).Auth.Credential, Assert.Single(second.Items).Auth.Credential);
        Assert.NotEqual(Assert.Single(first.Items).Auth.Credential, Assert.Single(reread.Items).Auth.Credential);
    }

    [Fact]
    public async Task ProofBindsTheExactSourceIssuerAndMessage()
    {
        using var bench = new Bench();
        var endpoint = ConfigDefaults.Fresh("vpn");
        var feature = await OfferAsync(bench, endpoint, 7).ConfigureAwait(false);
        var proof = Assert.IsType<ProxySourceProof>(feature.Proof);
        var anotherIssuer = Curve25519.Create().PublicKey;

        Assert.Equal(Guid.Parse(feature.Source).ToString("D"), feature.Source);
        Assert.Equal(64, Convert.FromBase64String(proof.Signature).Length);
        Assert.True(Verify(proof, feature.Source, endpoint.PublicKey));
        Assert.False(Verify(proof, Guid.NewGuid().ToString("D"), endpoint.PublicKey));
        Assert.False(Verify(proof, feature.Source, anotherIssuer));
        Assert.False(Verify(proof with { Issuer = anotherIssuer }, feature.Source, anotherIssuer));
        Assert.False(Verify(proof, feature.Source, endpoint.PublicKey, "\n"));
    }

    [Fact]
    public async Task IdentitySurvivesScopesAndAServiceProviderRestart()
    {
        using var bench = new Bench();
        var first = await new TransportProxyStore(bench.Db).IdentityAsync(default).ConfigureAwait(false);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServerDatabase(bench.DatabasePath, new AuthOptions());
        using var restarted = services.BuildServiceProvider();
        using var scope = restarted.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<TransportProxyStore>();
        var second = await store.IdentityAsync(default).ConfigureAwait(false);

        Assert.Equal(1, second.IdentityVersion);
        Assert.Equal(first.Source, second.Source);
        Assert.Equal(first.IdentityKey, second.IdentityKey);
        Assert.Equal(first.Source, await store.SourceAsync(default).ConfigureAwait(false));
        var endpoint = ConfigDefaults.Fresh("vpn");
        var before = await OfferAsync(bench, endpoint, 7).ConfigureAwait(false);
        var after = Assert.IsType<ProxiesFeature>(await new ProxyOffer(restarted.GetRequiredService<IServiceScopeFactory>())
            .OfferAsync(Peer(endpoint, 7), default).ConfigureAwait(false));
        var beforeProof = Assert.IsType<ProxySourceProof>(before.Proof);
        var afterProof = Assert.IsType<ProxySourceProof>(after.Proof);
        Assert.Equal(before.Source, after.Source);
        Assert.Equal(beforeProof.PublicKey, afterProof.PublicKey);
        Assert.True(Verify(afterProof, after.Source, endpoint.PublicKey));
    }

    [Fact]
    public async Task ConcurrentScopesInitializeOneIdentity()
    {
        using var bench = new Bench();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await ready.Task.ConfigureAwait(false);
            using var scope = bench.Scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<TransportProxyStore>().IdentityAsync(default).ConfigureAwait(false);
        })).ToArray();
        ready.SetResult();
        var identities = await Task.WhenAll(pending).ConfigureAwait(false);

        Assert.Single(identities.Select(identity => identity.Source).Distinct());
        Assert.Single(identities.Select(identity => identity.IdentityKey).Distinct());
        Assert.Equal(1, await bench.Db.ProxySources.CountAsync().ConfigureAwait(false));
    }

    [Fact]
    public async Task AnUnsignedSourceReceivesOneKeyWithoutChangingItsUuid()
    {
        using var bench = new Bench();
        var source = Guid.NewGuid().ToString("D").ToUpperInvariant();
        bench.Db.ProxySources.Add(new ProxySourceEntity { Id = 1, Source = source });
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var store = new TransportProxyStore(bench.Db);
        var first = await store.IdentityAsync(default).ConfigureAwait(false);
        var second = await store.IdentityAsync(default).ConfigureAwait(false);
        var persisted = await bench.Db.ProxySources.AsNoTracking().SingleAsync().ConfigureAwait(false);

        Assert.Equal(source.ToLowerInvariant(), first.Source);
        Assert.Equal(first.Source, second.Source);
        Assert.Equal(first.IdentityKey, second.IdentityKey);
        Assert.Equal(source, persisted.Source);
        Assert.Equal(1, persisted.IdentityVersion);
        Assert.Equal(first.IdentityKey, persisted.IdentityKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task InvalidStoredIdentityIsNeverReplaced(int kind)
    {
        using var bench = new Bench();
        using var key = ECDsa.Create(kind == 2 ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256);
        var encoded = key.ExportPkcs8PrivateKey();
        var invalid = kind switch
        {
            0 => string.Empty,
            1 => "invalid-base64",
            3 => Convert.ToBase64String(encoded.Append((byte)0).ToArray()),
            _ => Convert.ToBase64String(encoded),
        };
        var version = kind == 4 ? 0 : kind == 5 ? 2 : 1;
        var source = Guid.NewGuid().ToString("D");
        bench.Db.ProxySources.Add(new ProxySourceEntity
        {
            Id = 1, Source = source, IdentityKey = invalid, IdentityVersion = version,
        });
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var store = new TransportProxyStore(bench.Db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.IdentityAsync(default)).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.IdentityAsync(default)).ConfigureAwait(false);
        var persisted = await bench.Db.ProxySources.AsNoTracking().SingleAsync().ConfigureAwait(false);
        Assert.Equal(source, persisted.Source);
        Assert.Equal(invalid, persisted.IdentityKey);
        Assert.Equal(version, persisted.IdentityVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AnInvalidIssuerReceivesNoProof(int kind)
    {
        using var bench = new Bench();
        var endpoint = ConfigDefaults.Fresh("vpn");
        var invalid = kind switch
        {
            0 => endpoint with { PublicKey = "invalid-public-key" },
            1 => endpoint with { PrivateKey = string.Empty },
            2 => endpoint with { PublicKey = Curve25519.Create().PublicKey },
            _ => endpoint with { PublicKey = string.Empty, PrivateKey = string.Empty },
        };
        var feature = await OfferAsync(bench, invalid, 7).ConfigureAwait(false);

        Assert.Null(feature.Proof);
    }

    [Fact]
    public async Task TheFeaturePublishesOnlyThePublicCatalogKey()
    {
        using var bench = new Bench();
        var endpoint = ConfigDefaults.Fresh("vpn");
        var feature = await OfferAsync(bench, endpoint, 7).ConfigureAwait(false);
        var identity = await new TransportProxyStore(bench.Db).IdentityAsync(default).ConfigureAwait(false);
        var proof = Assert.IsType<ProxySourceProof>(feature.Proof);
        var text = JsonSerializer.Serialize(feature, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bytes = Convert.FromBase64String(proof.PublicKey);
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(bytes, out var read);

        Assert.Equal(bytes.Length, read);
        Assert.Equal(256, key.KeySize);
        Assert.Equal(ECCurve.NamedCurves.nistP256.Oid.Value, key.ExportParameters(false).Curve.Oid.Value);
        Assert.Contains("\"proof\":", text);
        Assert.DoesNotContain(identity.IdentityKey, text);
        Assert.DoesNotContain(endpoint.PrivateKey, text);
        Assert.DoesNotContain("identityKey", text);
        Assert.NotEqual(identity.IdentityKey, proof.PublicKey);
    }

    private static async Task<ProxiesFeature> OfferAsync(Bench bench, ServerConfig endpoint, long client,
        ClientTemplate? template = null) => Assert.IsType<ProxiesFeature>(await new ProxyOffer(bench.Scopes)
            .OfferAsync(Peer(endpoint, client, template), default).ConfigureAwait(false));

    private static HelloPeer Peer(ServerConfig endpoint, long client, ClientTemplate? template = null) => new(
        new TunnelClient { Id = client, Name = "client" }, endpoint, template, new DefaultHttpContext());

    private static bool Verify(ProxySourceProof proof, string source, string issuer, string suffix = "")
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(proof.PublicKey), out _);
        var body = Encoding.UTF8.GetBytes("amneziageo.proxy-source.v1\n" + source + "\n" + issuer + suffix);
        return key.VerifyData(body, Convert.FromBase64String(proof.Signature), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
