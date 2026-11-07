using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Publishes the proxies selected by the client template.
/// </summary>
public sealed class ProxyOffer : IHelloFeature
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyOffer(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.Proxies;

    /// <inheritdoc/>
    public async ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<TransportProxyStore>();
        var identity = await store.IdentityAsync(ct).ConfigureAwait(false);
        var selected = await store.SelectedAsync(peer.Template?.ProxyIds ?? [], ct).ConfigureAwait(false);
        var named = selected.Select(proxy => (Proxy: proxy, Host: proxy.Host.Length > 0 ? proxy.Host : peer.Context.Request.Host.Host.Trim('[', ']')))
            .Where(item => Uri.CheckHostName(item.Host) != UriHostNameType.Unknown);
        var items = named.Where(item => item.Proxy.Transport is "websocket" or "xhttp").Select(item =>
        {
            var proxy = item.Proxy;
            return
            new ProxyItem(
                proxy.Id.ToString(CultureInfo.InvariantCulture), proxy.Name, proxy.Transport,
                item.Host, proxy.Port, proxy.Path,
                new ProxyTls(proxy.ServerName.Length > 0 ? proxy.ServerName : item.Host, proxy.Pin),
                new ProxyAuth(proxy.Transport == "xhttp" ? "vless" : "bearer", TransportProxyStore.Credential(proxy, peer.Client.Id)),
                proxy.UpdatedUtc,
                proxy.Transport == "xhttp" ? new ProxyXHttp("packet-up", "GET", "header") : null,
                ProxyTargetRules.Read(proxy.TargetsJson) is { Restricted: true } targets ? targets : null);
        }).ToArray();
        return new ProxiesFeature(1, identity.Source, items, Proof(identity, peer));
    }

    private static ProxySourceProof? Proof(ProxySourceEntity identity, HelloPeer peer)
    {
        var issuer = peer.Endpoint.PublicKey;
        if (!Curve25519.IsKey(issuer) || !Curve25519.IsKey(peer.Endpoint.PrivateKey)
            || !string.Equals(issuer, Curve25519.PublicOf(peer.Endpoint.PrivateKey), StringComparison.Ordinal))
        {
            return null;
        }

        var bytes = Convert.FromBase64String(identity.IdentityKey);
        try
        {
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(bytes, out var read);
            if (read != bytes.Length)
            {
                throw new InvalidOperationException("the proxy catalog identity key is invalid");
            }

            var body = Encoding.UTF8.GetBytes("amneziageo.proxy-source.v1\n" + identity.Source + "\n" + issuer);
            var signature = key.SignData(body, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return new ProxySourceProof(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), issuer,
                Convert.ToBase64String(signature));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

/// <summary>
/// Describes the proxy catalog published by a server.
/// </summary>
public sealed record ProxiesFeature(int Version, string Source, IReadOnlyList<ProxyItem> Items, ProxySourceProof? Proof = null);

/// <summary>
/// Binds the catalog identity to a WireGuard interface.
/// </summary>
public sealed record ProxySourceProof(string PublicKey, string Issuer, string Signature);

/// <summary>
/// Describes a public transport proxy.
/// </summary>
public sealed record ProxyItem(
    string Id, string Name, string Transport, string Host, int Port, string Path,
    ProxyTls Tls, ProxyAuth Auth, DateTimeOffset Updated, ProxyXHttp? Xhttp = null, ProxyTargets? Targets = null);

/// <summary>
/// Describes the TLS identity of a proxy.
/// </summary>
public sealed record ProxyTls(string ServerName, string Pin);

/// <summary>
/// Carries the transport credential of a client.
/// </summary>
public sealed record ProxyAuth(string Scheme, string Credential);

/// <summary>
/// Describes the HTTP requests of an XHTTP proxy.
/// </summary>
public sealed record ProxyXHttp(
    string Mode,
    [property: System.Text.Json.Serialization.JsonPropertyName("uplinkHTTPMethod")] string UplinkHttpMethod,
    string UplinkDataPlacement);
