using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Net;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Selects proxy certificates on shared ports.
/// </summary>
public sealed class ProxyCertificates
{
    private IReadOnlyDictionary<(int Port, string Host), Identity> _certificates = new Dictionary<(int, string), Identity>();
    private IReadOnlyList<(int Port, Identity Identity)> _identities = [];

    /// <summary>
    /// Updates the certificates of the enabled proxies.
    /// </summary>
    public void Update(IEnumerable<TransportProxyEntity> proxies, string chain = "", string key = "")
    {
        proxies = proxies.Where(proxy => proxy.IsEnabled && proxy.Hosted && proxy.UsesTls && (proxy.Transport == "websocket" || proxy.IsHttpV2)).ToArray();
        var selected = new Dictionary<(int, string), Identity>();
        var identities = new List<(int, Identity)>();
        foreach (var proxy in proxies)
        {
            var certificate = proxy.Certificate.Length > 0 ? proxy.Certificate : chain;
            var privateKey = proxy.CertificateKey.Length > 0 ? proxy.CertificateKey : proxy.Certificate.Length > 0 ? certificate : key.Length > 0 ? key : certificate;
            if (certificate.Length == 0)
            {
                continue;
            }

            var identity = new Identity(proxy, new WebCertificate(certificate, privateKey), chain, key);
            var port = proxy.ListenPort == 0 ? proxy.Port : proxy.ListenPort;
            identities.Add((port, identity));
            selected.TryAdd((port, proxy.Host.ToLowerInvariant()), identity);
            if (proxy.ServerName.Length > 0)
            {
                selected.TryAdd((port, proxy.ServerName.ToLowerInvariant()), identity);
            }
        }

        Volatile.Write(ref _certificates, selected);
        Volatile.Write(ref _identities, identities);
    }

    /// <summary>
    /// Reports whether a port serves an HTTP origin.
    /// </summary>
    public bool AllowsHttp(int port) => false;

    /// <summary>
    /// Selects the default IP certificate.
    /// </summary>
    public X509Certificate2? Default(int port)
    {
        var candidates = Volatile.Read(ref _identities).Where(pair => pair.Port == port).Select(pair => pair.Identity)
            .Distinct().Where(identity => IPAddress.TryParse(identity.Proxy.ServerName.Length > 0 ? identity.Proxy.ServerName : identity.Proxy.Host, out _)
                && identity.IsValid)
            .Select(identity => identity.Current()).DistinctBy(certificate => certificate.GetCertHashString(HashAlgorithmName.SHA256))
            .Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    /// <summary>
    /// Returns the certificate for a public proxy host.
    /// </summary>
    public X509Certificate2? Pick(int port, string? host)
    {
        var certificates = Volatile.Read(ref _certificates);
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        if (certificates.TryGetValue((port, host.ToLowerInvariant()), out var direct))
        {
            return direct.Current();
        }

        foreach (var (entry, certificate) in certificates.Where(pair => pair.Key.Port == port))
        {
            if (!certificate.IsValid)
            {
                continue;
            }
            var current = certificate.Current();
            if (current.MatchesHostname(host))
            {
                return current;
            }
        }

        return null;
    }

    private sealed record Identity(TransportProxyEntity Proxy, WebCertificate Certificate, string Chain, string Key)
    {
        public bool IsValid => ProxySecurity.Check(Proxy, Chain, Key) is null;

        public X509Certificate2 Current()
        {
            if (ProxySecurity.Check(Proxy, Chain, Key) is { } fault)
            {
                throw new InvalidOperationException(fault.Code);
            }

            return Certificate.Current();
        }
    }
}
