using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AmneziaGeo.Server.Tests;

internal sealed class ServiceTlsFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "service-tls-" + Guid.NewGuid().ToString("N"));

    public string Chain => Path.Combine(_root, "chain.pem");
    public string Key => Path.Combine(_root, "key.pem");
    public string Pin { get; }
    public WebOptions Options => new() { Certificate = Chain, CertificateKey = Key };

    /// <summary>
    /// ctor
    /// </summary>
    public ServiceTlsFixture()
    {
        Directory.CreateDirectory(_root);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        foreach (var host in new[] { "localhost", "vpn.example", "vpn.example.net", "vpn.example.org", "proxy.example.net" })
        {
            names.AddDnsName(host);
        }

        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        Pin = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        File.WriteAllText(Chain, certificate.ExportCertificatePem());
        File.WriteAllText(Key, key.ExportPkcs8PrivateKeyPem());
    }

    public TransportProxyEntity Secure(TransportProxyEntity proxy)
    {
        proxy.Host = proxy.Host.Length == 0 ? "localhost" : proxy.Host;
        proxy.UseCertificate = true;
        proxy.OriginHttp = false;
        proxy.Certificate = Chain;
        proxy.CertificateKey = Key;
        proxy.Pin = Pin;
        return proxy;
    }

    public async Task PrepareLegacyAsync(Bench bench)
    {
        var store = new TransportProxyStore(bench.Db);
        await store.MigrateLegacyAsync(default).ConfigureAwait(false);
        foreach (var proxy in (await store.ListAsync(default).ConfigureAwait(false)).Where(proxy => proxy.LegacyConfigId is not null))
        {
            await store.SaveAsync(proxy.Id, Secure(proxy), default).ConfigureAwait(false);
        }
    }

    public bool Accepts(X509Certificate2 certificate, string name) =>
        ProxySecurity.Check(certificate, new X509Certificate2Collection(certificate), name, Pin, DateTimeOffset.UtcNow) is null;

    public static void SecureConnection(HttpContext context) =>
        context.Features.Set<ITlsConnectionFeature>(new TlsConnection());

    public HttpClient Client() => new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (request, certificate, _, _) => certificate is not null
            && Accepts(certificate, request.RequestUri!.IdnHost),
    });

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class TlsConnection : ITlsConnectionFeature
    {
        public X509Certificate2? ClientCertificate { get; set; }
        public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) => Task.FromResult(ClientCertificate);
    }
}
