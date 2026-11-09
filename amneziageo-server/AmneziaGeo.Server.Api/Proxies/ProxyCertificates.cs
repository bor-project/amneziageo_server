using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Selects proxy certificates on shared ports.
/// </summary>
public sealed class ProxyCertificates
{
    private IReadOnlySet<int> _plain = new HashSet<int>();

    private IReadOnlyDictionary<(int Port, string Host), WebCertificate> _certificates = new Dictionary<(int, string), WebCertificate>();

    /// <summary>
    /// Updates the certificates of the enabled proxies.
    /// </summary>
    public void Update(IEnumerable<TransportProxyEntity> proxies)
    {
        proxies = proxies.Where(proxy => proxy.IsEnabled && proxy.Hosted && (proxy.Transport == "websocket" || proxy.IsHttpV2)).ToArray();
        var selected = new Dictionary<(int, string), WebCertificate>();
        foreach (var proxy in proxies.Where(proxy => proxy.UsesCertificate && proxy.Certificate.Length > 0))
        {
            var key = (proxy.ListenPort == 0 ? proxy.Port : proxy.ListenPort, proxy.Host.ToLowerInvariant());
            selected.TryAdd(key, new WebCertificate(proxy.Certificate,
                proxy.CertificateKey.Length > 0 ? proxy.CertificateKey : proxy.Certificate));
        }

        Volatile.Write(ref _certificates, selected);
        Volatile.Write(ref _plain, proxies.Where(proxy => proxy.OriginHttp || !proxy.UsesCertificate)
            .Select(proxy => proxy.ListenPort == 0 ? proxy.Port : proxy.ListenPort).ToHashSet());
    }

    /// <summary>
    /// Reports whether a port serves an HTTP origin.
    /// </summary>
    public bool AllowsHttp(int port) => Volatile.Read(ref _plain).Contains(port);

    /// <summary>
    /// Returns the certificate for a public proxy host.
    /// </summary>
    public X509Certificate2? Pick(int port, string? host)
    {
        var certificates = Volatile.Read(ref _certificates);
        if (host is null)
        {
            return null;
        }

        if (certificates.TryGetValue((port, host.ToLowerInvariant()), out var direct))
        {
            return direct.Current();
        }

        foreach (var (entry, certificate) in certificates.Where(pair => pair.Key.Port == port))
        {
            var current = certificate.Current();
            if (current.MatchesHostname(host))
            {
                return current;
            }
        }

        return null;
    }
}
