using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

internal sealed class ProxyTlsFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "proxy-tls-" + Guid.NewGuid().ToString("N"));
    public string Chain => Path.Combine(DirectoryPath, "chain.pem");
    public string Key => Path.Combine(DirectoryPath, "key.pem");
    public string Pin { get; }
    public string Thumbprint { get; }
    public X509Certificate2 Certificate { get; }

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyTlsFixture(string host = "proxy.example.net", DateTimeOffset? before = null, DateTimeOffset? after = null, bool serverAuth = true)
    {
        Directory.CreateDirectory(DirectoryPath);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + host, key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(host, out var address))
        {
            names.AddIpAddress(address);
        }
        else
        {
            names.AddDnsName(host);
        }
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(serverAuth ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, false));
        Certificate = request.CreateSelfSigned(before ?? DateTimeOffset.UtcNow.AddDays(-1), after ?? DateTimeOffset.UtcNow.AddDays(1));
        Pin = Convert.ToHexString(SHA256.HashData(Certificate.RawData));
        Thumbprint = Certificate.Thumbprint;
        File.WriteAllText(Chain, Certificate.ExportCertificatePem());
        File.WriteAllText(Key, key.ExportPkcs8PrivateKeyPem());
    }

    public TransportProxyEntity Proxy(string transport = "websocket") => new()
    {
        Name = "secure", Host = Certificate.GetNameInfo(X509NameType.SimpleName, false), Port = 8443, Path = "relay",
        Transport = transport, ProtocolVersion = transport == "http" ? 2 : 0, UseCertificate = true,
        Certificate = Chain, CertificateKey = Key, Pin = Pin,
    };

    public void Dispose()
    {
        Certificate.Dispose();
        Directory.Delete(DirectoryPath, true);
    }
}
