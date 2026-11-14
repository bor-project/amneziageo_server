using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Api.Web;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Validates proxy TLS identities.
/// </summary>
public static class ProxySecurity
{
    /// <summary>
    /// Validates proxy settings and certificate files.
    /// </summary>
    public static PanelFault? Check(TransportProxyEntity proxy, string chain = "", string key = "", ILogger? logger = null, string publicName = "")
    {
        var unnamed = proxy.LegacyConfigId is not null && proxy.Host.Length == 0;
        if (ProxyRules.Check(unnamed ? Named(proxy, publicName.Length > 0 ? publicName : "localhost") : proxy) is { } rules)
        {
            return new(rules.Error, rules.Message);
        }

        if (!proxy.Hosted)
        {
            return null;
        }

        var certificate = proxy.Certificate.Length > 0 ? proxy.Certificate : chain;
        var privateKey = proxy.CertificateKey.Length > 0 ? proxy.CertificateKey : proxy.Certificate.Length > 0 ? certificate : key.Length > 0 ? key : certificate;
        if (certificate.Length == 0 || privateKey.Length == 0)
        {
            return new("proxy-certificate-missing", "the proxy requires a TLS certificate and private key");
        }

        try
        {
            var chainText = WebCertificate.Read(certificate);
            using var leaf = X509Certificate2.CreateFromPem(chainText, WebCertificate.Read(privateKey));
            var certificates = new X509Certificate2Collection();
            try
            {
                certificates.ImportFromPem(chainText);
                var name = proxy.ServerName.Length > 0 ? proxy.ServerName
                    : unnamed ? publicName.Length > 0 ? publicName : Name(leaf) : proxy.Host;
                return Check(leaf, certificates, name, proxy.Pin, DateTimeOffset.UtcNow);
            }
            finally
            {
                foreach (var item in certificates)
                {
                    item.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            (logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).LogWarning(ex, "the proxy certificate or private key cannot be loaded");
            return new("proxy-certificate-unreadable", "the proxy certificate or private key cannot be loaded");
        }
    }

    /// <summary>
    /// Validates a certificate identity.
    /// </summary>
    public static PanelFault? Check(X509Certificate2 leaf, X509Certificate2Collection certificates, string name, string pin,
        DateTimeOffset now, X509Certificate2Collection? roots = null)
    {
        if (now.UtcDateTime < leaf.NotBefore.ToUniversalTime() || now.UtcDateTime > leaf.NotAfter.ToUniversalTime())
        {
            return new("proxy-certificate-expired", "the proxy certificate is outside its validity period");
        }

        if (!leaf.MatchesHostname(name.Trim('[', ']'), allowWildcards: true, allowCommonName: false))
        {
            return new("proxy-certificate-name", "the proxy certificate does not match its TLS name");
        }

        var pinned = pin.Length == 64 && pin.All(char.IsAsciiHexDigit)
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(leaf.RawData), Convert.FromHexString(pin));
        if (pin.Length > 0 && !pinned)
        {
            return new("proxy-certificate-pin", "the proxy certificate does not match its trusted pin");
        }

        using var trust = new X509Chain();
        trust.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        trust.ChainPolicy.DisableCertificateDownloads = true;
        trust.ChainPolicy.VerificationTime = now.UtcDateTime;
        trust.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        trust.ChainPolicy.ExtraStore.AddRange(certificates);
        if (roots is not null)
        {
            trust.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            trust.ChainPolicy.CustomTrustStore.AddRange(roots);
        }

        if (!trust.Build(leaf) && (!pinned || trust.ChainStatus.Any(status =>
            (status.Status & ~(X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.PartialChain)) != 0)))
        {
            return new("proxy-certificate-untrusted", "the proxy certificate is not trusted");
        }

        return null;
    }

    /// <summary>
    /// Returns catalog entry settings.
    /// </summary>
    public static TransportProxyEntity Of(ProxyServiceEntry entry) => new()
    {
        Id = entry.Id, Name = entry.Name, Transport = entry.Transport, Host = entry.Host, Port = entry.Port, Path = entry.Path,
        ListenPort = entry.ListenPort, UseCertificate = entry.UseCertificate, OriginHttp = entry.OriginHttp,
        HttpMethod = entry.HttpMethod, ProtocolVersion = entry.ProtocolVersion, Certificate = entry.Certificate,
        CertificateKey = entry.CertificateKey, ServerName = entry.ServerName, Pin = entry.Pin,
        TargetsJson = AmneziaGeo.Server.Core.Proxy.ProxyTargetRules.Write(entry.Targets),
        AllowAnonymous = entry.AllowAnonymous,
    };

    /// <summary>
    /// Selects usable catalog entries.
    /// </summary>
    public static (ProxyCatalog Catalog, IReadOnlyDictionary<long, string> Faults) Active(ProxyCatalog catalog, ILogger? logger = null)
    {
        ProxyCatalog.Check(catalog, preserveInvalidRecords: true);
        var faults = new Dictionary<long, string>();
        foreach (var entry in catalog.Proxies)
        {
            if (Check(Of(entry), logger: logger) is { } fault)
            {
                faults[entry.Id] = fault.Code;
            }
        }

        return (catalog with { Proxies = catalog.Proxies.Where(entry => !faults.ContainsKey(entry.Id)).ToArray() }, faults);
    }

    /// <summary>
    /// Selects a concrete certificate name.
    /// </summary>
    public static string Name(X509Certificate2 certificate)
    {
        var names = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        return names?.EnumerateDnsNames().FirstOrDefault(name => !name.Contains('*'))
            ?? names?.EnumerateIPAddresses().FirstOrDefault()?.ToString() ?? "";
    }

    private static TransportProxyEntity Named(TransportProxyEntity proxy, string name) => new()
    {
        Name = proxy.Name, Transport = proxy.Transport, Host = name, Port = proxy.Port, ListenPort = proxy.ListenPort,
        OriginHttp = proxy.OriginHttp, UseCertificate = proxy.UseCertificate, HttpMethod = proxy.HttpMethod,
        ProtocolVersion = proxy.ProtocolVersion, Path = proxy.Path, ServerName = proxy.ServerName, Pin = proxy.Pin,
        TargetsJson = proxy.TargetsJson,
        AllowAnonymous = proxy.AllowAnonymous,
    };
}
