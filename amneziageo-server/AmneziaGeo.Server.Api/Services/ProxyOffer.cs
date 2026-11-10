using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Publishes the proxies selected by the client template.
/// </summary>
public sealed class ProxyOffer : IHelloFeature
{
    private readonly IServiceScopeFactory _scopes;
    private readonly bool _modern;
    private readonly FrontOptions? _options;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyOffer(IServiceScopeFactory scopes, bool modern = false, FrontOptions? options = null)
    {
        _scopes = scopes;
        _modern = modern;
        _options = options;
    }

    /// <inheritdoc/>
    public string Name => _modern ? "proxiesV2" : FeatureNames.Proxies;

    /// <inheritdoc/>
    public async ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<TransportProxyStore>();
        var identity = await store.IdentityAsync(ct).ConfigureAwait(false);
        var selected = _options?.Enabled == false ? [] : await store.SelectedAsync(peer.Template?.ProxyIds ?? [], ct).ConfigureAwait(false);
        var panel = scope.ServiceProvider.GetService<PanelSettings>() ?? new PanelSettings();
        var options = scope.ServiceProvider.GetService<WebOptions>() ?? new WebOptions();
        selected = selected.Where(proxy => ProxySecurity.Check(proxy, Listening.Chain(options, panel), Listening.Key(options, panel),
            publicName: peer.Context.Request.Host.Host.Trim('[', ']')) is null).ToArray();
        var named = selected.Select(proxy => (Proxy: proxy, Host: proxy.Host.Length > 0 ? proxy.Host : peer.Context.Request.Host.Host.Trim('[', ']')))
            .Where(item => Uri.CheckHostName(item.Host) != UriHostNameType.Unknown);
        if (_modern)
        {
            var modern = named.Where(item => item.Proxy.Transport is "websocket" or "quic" || item.Proxy.IsHttpV2).Select(item =>
            {
                var proxy = item.Proxy;
                return new ProxyItemV2(proxy.Id.ToString(CultureInfo.InvariantCulture), proxy.Name,
                    proxy.IsHttp ? "http" : proxy.Transport, item.Host, proxy.Port, proxy.Path,
                    new ProxyTlsV2(proxy.UsesCertificate, proxy.ServerName.Length > 0 ? proxy.ServerName : item.Host, proxy.UsesCertificate ? proxy.Pin : string.Empty),
                    new ProxyAuth(proxy.IsHttp ? "hmac-sha256-v2" : "bearer", TransportProxyStore.Credential(proxy, peer.Client.Id)),
                    proxy.UpdatedUtc, proxy.IsHttp ? new ProxyHttp(2, proxy.HttpMethod, proxy.HttpMethod == "GET" ? "header" : "body") : null,
                    proxy.Transport == "quic" ? new ProxyQuic(1, "webtransport", "framed-stream") : null,
                    ProxyTargetRules.Read(proxy.TargetsJson) is { Restricted: true } targets ? targets : null);
            }).ToArray();
            return new ProxiesFeatureV2(2, identity.Source, modern, Proof(identity, peer));
        }

        var items = named.Where(item => item.Proxy.Transport == "websocket" && item.Proxy.UsesCertificate
            || item.Proxy.IsHttpV2 && item.Proxy.HttpMethod == "GET").Select(item =>
        {
            var proxy = item.Proxy;
            return
            new ProxyItem(
                proxy.Id.ToString(CultureInfo.InvariantCulture), proxy.Name, proxy.IsHttp ? "xhttp" : proxy.Transport,
                item.Host, proxy.Port, proxy.Path,
                new ProxyTls(proxy.ServerName.Length > 0 ? proxy.ServerName : item.Host, proxy.UsesCertificate ? proxy.Pin : string.Empty),
                new ProxyAuth(proxy.IsHttp ? "vless" : "bearer", TransportProxyStore.Credential(proxy, peer.Client.Id)),
                proxy.UpdatedUtc,
                proxy.IsHttp ? new ProxyXHttp("packet-up", "GET", "header", proxy.UsesCertificate ? "https" : "http") : null,
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
    string UplinkDataPlacement, string Scheme = "https", int Version = 2);


public sealed record ProxiesFeatureV2(int Version, string Source, IReadOnlyList<ProxyItemV2> Items, ProxySourceProof? Proof);

public sealed record ProxyItemV2(string Id, string Name, string Transport, string Host, int Port, string Path,
    ProxyTlsV2 Tls, ProxyAuth Auth, DateTimeOffset Updated, ProxyHttp? Http, ProxyQuic? Quic, ProxyTargets? Targets);

public sealed record ProxyTlsV2(bool Enabled, string ServerName, string Pin);

public sealed record ProxyHttp(int Version, string Method, string DataPlacement);

public sealed record ProxyQuic(int Version, string Protocol, string Datagrams);
