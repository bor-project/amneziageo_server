using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Exports configured service certificate identities.
/// </summary>
public static class ServiceTrust
{
    /// <summary>
    /// Exports the selected service TLS identity.
    /// </summary>
    public static ServiceTlsIdentity? Of(ServerConfig endpoint, WebOptions options, PanelSettings panel, ILogger? logger = null,
        string publicName = "", SubscriptionSettings? subscription = null, ProxyCertificates? proxies = null,
        IReadOnlyList<TransportProxyEntity>? configured = null, ServiceCertificateFiles? selectedFiles = null)
    {
        var port = ConfigServices.Port(endpoint);
        var panelChain = Listening.Chain(options, panel);
        var panelKey = Listening.Key(options, panel);
        var separate = subscription is { IsEnabled: true, Separate: true } && subscription.Port == port
            && port != panel.Port && subscription.Certificate.Length > 0;
        var file = selectedFiles?.Chain ?? (separate ? subscription!.Certificate : panelChain);
        var key = selectedFiles?.Key ?? (separate ? subscription!.CertificateKey : panelKey);
        var selector = proxies ?? new ProxyCertificates();
        if (proxies is null && configured is not null)
        {
            selector.Update(configured, panelChain, panelKey);
        }

        var certificates = new X509Certificate2Collection();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var fallback = default(X509Certificate2);
        try
        {
            var files = new HashSet<string>(StringComparer.Ordinal);
            if (file.Length > 0)
            {
                files.Add(file);
            }

            foreach (var proxy in configured ?? [])
            {
                if (proxy.IsEnabled && proxy.Hosted && proxy.UsesTls && (proxy.Transport == "websocket" || proxy.IsHttpV2))
                {
                    files.Add(proxy.Certificate.Length > 0 ? proxy.Certificate : panelChain);
                }
            }

            foreach (var path in files.Where(path => path.Length > 0))
            {
                var chain = new X509Certificate2Collection();
                try
                {
                    chain.ImportFromPem(WebCertificate.Read(path));
                    if (chain.Count > 0)
                    {
                        known.Add(chain[0].GetCertHashString(HashAlgorithmName.SHA256));
                    }

                    certificates.AddRange(chain);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
                {
                    foreach (var item in chain)
                    {
                        item.Dispose();
                    }
                    (logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).LogDebug(ex, "the service certificate chain cannot be read");
                }
            }

            var host = (endpoint.Host.Length > 0 ? endpoint.Host : publicName).Trim('[', ']');
            var selected = Select(host);
            if (selected is null)
            {
                var candidates = certificates.Cast<X509Certificate2>()
                    .Where(certificate => known.Contains(certificate.GetCertHashString(HashAlgorithmName.SHA256)))
                    .Select(ProxySecurity.Name).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => (Name: name, Certificate: Select(name)))
                    .Where(candidate => candidate.Certificate is not null
                        && known.Contains(candidate.Certificate.GetCertHashString(HashAlgorithmName.SHA256)))
                    .DistinctBy(candidate => candidate.Certificate!.GetCertHashString(HashAlgorithmName.SHA256))
                    .Take(2).ToArray();
                if (candidates.Length != 1)
                {
                    return null;
                }

                host = candidates[0].Name;
                selected = candidates[0].Certificate!;
            }

            var name = host.Length > 0 && selected.MatchesHostname(host, allowWildcards: true, allowCommonName: false)
                ? host : ProxySecurity.Name(selected);
            if (name.Length == 0)
            {
                return null;
            }

            selected = Select(name);
            return selected is not null && known.Contains(selected.GetCertHashString(HashAlgorithmName.SHA256))
                ? Of(selected, certificates, name) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or InvalidOperationException)
        {
            (logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).LogWarning(ex, "the service certificate identity cannot be exported");
            return null;
        }
        finally
        {
            fallback?.Dispose();
            foreach (var certificate in certificates)
            {
                certificate.Dispose();
            }
        }

        X509Certificate2? Select(string name)
        {
            var sni = IPAddress.TryParse(name, out _) || name.Length == 0 ? null : name;
            var selected = selector.Pick(port, sni);
            if (selected is not null)
            {
                return selected;
            }

            if (file.Length == 0)
            {
                return sni is null ? selector.Default(port) : null;
            }

            fallback ??= X509Certificate2.CreateFromPem(WebCertificate.Read(file), WebCertificate.Read(key.Length > 0 ? key : file));
            return fallback;
        }
    }

    /// <summary>
    /// Exports a validated certificate identity.
    /// </summary>
    public static ServiceTlsIdentity? Of(X509Certificate2 leaf, X509Certificate2Collection certificates, string name,
        X509Certificate2Collection? roots = null)
    {
        var pin = Convert.ToHexString(SHA256.HashData(leaf.RawData)).ToLowerInvariant();
        if (ProxySecurity.Check(leaf, certificates, name, pin, DateTimeOffset.UtcNow, roots) is not null)
        {
            return null;
        }

        var trusted = ProxySecurity.Check(leaf, certificates, name, "", DateTimeOffset.UtcNow, roots) is null;
        return new ServiceTlsIdentity(name, trusted ? "" : pin);
    }
}


/// <summary>
/// Names the configured certificate files of a listener.
/// </summary>
public sealed record ServiceCertificateFiles(string Chain, string Key);
