using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Web;

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyTlsSecurityTests
{
    [Fact]
    public void HostedProxyRequiresTrustedCertificateOrExplicitPin()
    {
        using var fixture = new ProxyTlsFixture();
        var proxy = fixture.Proxy();
        Assert.Null(ProxySecurity.Check(proxy));
        proxy.Pin = "";
        Assert.Equal("proxy-certificate-untrusted", ProxySecurity.Check(proxy)!.Code);
        proxy.Pin = new string('0', 64);
        Assert.Equal("proxy-certificate-pin", ProxySecurity.Check(proxy)!.Code);
    }

    [Fact]
    public void ExplicitPinDoesNotBypassHostnameValidityOrServerPurpose()
    {
        using var valid = new ProxyTlsFixture();
        var wrongName = valid.Proxy();
        wrongName.Host = "other.example.net";
        Assert.Equal("proxy-certificate-name", ProxySecurity.Check(wrongName)!.Code);
        using var expired = new ProxyTlsFixture(before: DateTimeOffset.UtcNow.AddDays(-3), after: DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Equal("proxy-certificate-expired", ProxySecurity.Check(expired.Proxy())!.Code);
        using var future = new ProxyTlsFixture(before: DateTimeOffset.UtcNow.AddDays(1), after: DateTimeOffset.UtcNow.AddDays(2));
        Assert.Equal("proxy-certificate-expired", ProxySecurity.Check(future.Proxy())!.Code);
        using var clientOnly = new ProxyTlsFixture(serverAuth: false);
        Assert.Equal("proxy-certificate-untrusted", ProxySecurity.Check(clientOnly.Proxy())!.Code);
    }

    [Fact]
    public void HostedProxyChecksInheritedPanelIdentityAndPrivateKey()
    {
        using var fixture = new ProxyTlsFixture();
        var proxy = fixture.Proxy();
        proxy.Certificate = "";
        proxy.CertificateKey = "";
        Assert.Equal("proxy-certificate-missing", ProxySecurity.Check(proxy)!.Code);
        Assert.Null(ProxySecurity.Check(proxy, fixture.Chain, fixture.Key));
        using var other = new ProxyTlsFixture();
        Assert.Equal("proxy-certificate-unreadable", ProxySecurity.Check(proxy, fixture.Chain, other.Key)!.Code);
        proxy.Host = "other.example.net";
        Assert.Equal("proxy-certificate-name", ProxySecurity.Check(proxy, fixture.Chain, fixture.Key)!.Code);
    }

    [Fact]
    public void CertificateAuthorityTrustAcceptsServerIdentityWithoutPin()
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=fixture-root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=proxy.example.net", leafKey, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("proxy.example.net");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var leaf = request.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
        var chain = new X509Certificate2Collection(root);
        var roots = new X509Certificate2Collection(root);
        Assert.Null(ProxySecurity.Check(leaf, chain, "proxy.example.net", "", DateTimeOffset.UtcNow, roots));
        Assert.Equal("proxy-certificate-name", ProxySecurity.Check(leaf, chain, "other.example.net", "", DateTimeOffset.UtcNow, roots)!.Code);
        Assert.Equal("proxy-certificate-untrusted", ProxySecurity.Check(leaf, chain, "proxy.example.net", "", DateTimeOffset.UtcNow)!.Code);
    }

    [Fact]
    public void SameMtimeCertificateAndKeyRotationReloadsTheServedIdentity()
    {
        using var previous = new ProxyTlsFixture();
        using var replacement = new ProxyTlsFixture();
        var certificate = new WebCertificate(previous.Chain, previous.Key);
        Assert.Equal(previous.Thumbprint, certificate.Current().Thumbprint);
        var chainStamp = File.GetLastWriteTimeUtc(previous.Chain);
        var keyStamp = File.GetLastWriteTimeUtc(previous.Key);
        File.Copy(replacement.Chain, previous.Chain, true);
        File.Copy(replacement.Key, previous.Key, true);
        File.SetLastWriteTimeUtc(previous.Chain, chainStamp);
        File.SetLastWriteTimeUtc(previous.Key, keyStamp);
        Assert.Equal(replacement.Thumbprint, certificate.Current().Thumbprint);
    }

    [Fact]
    public void BrokenKeyRotationDoesNotReturnTheCachedCertificate()
    {
        using var fixture = new ProxyTlsFixture();
        var certificate = new WebCertificate(fixture.Chain, fixture.Key);
        Assert.Equal(fixture.Thumbprint, certificate.Current().Thumbprint);
        var stamp = File.GetLastWriteTimeUtc(fixture.Key);
        File.WriteAllText(fixture.Key, "invalid-key");
        File.SetLastWriteTimeUtc(fixture.Key, stamp);
        Assert.ThrowsAny<CryptographicException>(() => certificate.Current());
    }

    [Fact]
    public void CertificatePickerServesTheIdentityItValidatedAfterRotation()
    {
        using var previous = new ProxyTlsFixture();
        using var replacement = new ProxyTlsFixture();
        var proxy = previous.Proxy();
        var certificates = new ProxyCertificates();
        certificates.Update([proxy]);
        Assert.Equal(previous.Thumbprint, certificates.Pick(proxy.Port, proxy.Host)!.Thumbprint);
        var stamp = File.GetLastWriteTimeUtc(previous.Chain);
        File.Copy(replacement.Chain, previous.Chain, true);
        File.Copy(replacement.Key, previous.Key, true);
        File.SetLastWriteTimeUtc(previous.Chain, stamp);
        proxy.Pin = replacement.Pin;
        Assert.Equal(replacement.Thumbprint, certificates.Pick(proxy.Port, proxy.Host)!.Thumbprint);
        File.WriteAllText(previous.Key, "invalid-key");
        Assert.Throws<InvalidOperationException>(() => certificates.Pick(proxy.Port, proxy.Host));
    }

    [Fact]
    public void CertificatePickerLeavesMissingSniToTheSharedPanelIdentity()
    {
        using var fixture = new ProxyTlsFixture();
        var proxy = fixture.Proxy();
        var certificates = new ProxyCertificates();
        certificates.Update([proxy]);
        Assert.Null(certificates.Pick(proxy.Port, null));
        Assert.Null(certificates.Pick(proxy.Port, ""));
    }

    [Fact]
    public void DefaultIpIdentityRejectsAmbiguousAndBrokenCertificates()
    {
        using var first = new ProxyTlsFixture("127.0.0.1");
        using var second = new ProxyTlsFixture("127.0.0.2");
        using var sameHost = new ProxyTlsFixture("127.0.0.1");
        var certificates = new ProxyCertificates();
        var one = first.Proxy();
        var two = second.Proxy();
        two.Path = "second";
        certificates.Update([one]);
        Assert.Equal(first.Thumbprint, certificates.Default(one.Port)!.Thumbprint);
        certificates.Update([one, two]);
        Assert.Null(certificates.Default(one.Port));
        var same = sameHost.Proxy();
        same.Path = "same-host";
        certificates.Update([one, same]);
        Assert.Null(certificates.Default(one.Port));
        certificates.Update([one]);
        File.WriteAllText(first.Key, "invalid-key");
        Assert.Null(certificates.Default(one.Port));
        using var dns = new ProxyTlsFixture("proxy.example.net");
        certificates.Update([dns.Proxy()]);
        Assert.Null(certificates.Default(one.Port));
    }

    [Fact]
    public void StandaloneActiveCatalogRetainsBlockedRecordsAndRecoversWithoutChangingIdentity()
    {
        using var valid = new ProxyTlsFixture();
        using var blocked = new ProxyTlsFixture();
        var credentials = new[] { "40168f45-37b1-4bfa-903c-9904f48489ae" };
        var plain = new ProxyServiceEntry(1, "legacy", "http", "proxy.example.net", 8443, "legacy", credentials,
            UseCertificate: false, ProtocolVersion: 2, Certificate: valid.Chain, CertificateKey: valid.Key, Pin: valid.Pin);
        var secure = new ProxyServiceEntry(2, "secure", "http", "proxy.example.net", 8443, "secure", credentials,
            ProtocolVersion: 2, Certificate: valid.Chain, CertificateKey: valid.Key, Pin: valid.Pin);
        var broken = new ProxyServiceEntry(3, "broken", "http", "proxy.example.net", 8443, "broken", credentials,
            ProtocolVersion: 2, Certificate: blocked.Chain, CertificateKey: blocked.Key, Pin: blocked.Pin);
        var catalog = new ProxyCatalog(1, [plain, secure, broken]);
        var before = System.Text.Json.JsonSerializer.Serialize(catalog, ProxyCatalog.Json);
        var key = File.ReadAllText(blocked.Key);
        File.WriteAllText(blocked.Key, "invalid-key");
        Assert.Throws<InvalidDataException>(() => ProxyCatalog.Check(catalog));
        var first = ProxySecurity.Active(catalog);
        Assert.Equal(secure.Id, Assert.Single(first.Catalog.Proxies).Id);
        Assert.Same(secure, first.Catalog.Proxies[0]);
        Assert.Equal("proxy-tls-required", first.Faults[plain.Id]);
        Assert.Equal("proxy-certificate-unreadable", first.Faults[broken.Id]);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(catalog, ProxyCatalog.Json));
        File.WriteAllText(blocked.Key, key);
        var recovered = ProxySecurity.Active(catalog);
        Assert.Equal(new[] { secure.Id, broken.Id }, recovered.Catalog.Proxies.Select(entry => entry.Id));
        Assert.Single(recovered.Faults);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(catalog, ProxyCatalog.Json));
        var repaired = catalog with { Proxies = [plain with { UseCertificate = true, OriginHttp = false }, secure, broken] };
        ProxyCatalog.Check(repaired);
        var active = ProxySecurity.Active(repaired);
        Assert.Empty(active.Faults);
        Assert.Equal(catalog.Proxies.Select(entry => entry.Id), active.Catalog.Proxies.Select(entry => entry.Id));
        Assert.All(active.Catalog.Proxies, entry => Assert.Equal(credentials, entry.Credentials));
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(catalog, ProxyCatalog.Json));
    }

    [Fact]
    public void RawPublicQuicCommandRequiresCertificateConfiguration()
    {
        Assert.Throws<ArgumentException>(() => ProxyDaemon.NativeCommand(["server", "wts://[::]:443", "--restrict-config", "/tmp/rules.yml"]));
        Assert.Throws<ArgumentException>(() => ProxyDaemon.NativeCommand(["server", "ws://[::]:443", "--restrict-config", "/tmp/rules.yml"]));
        Assert.NotNull(ProxyDaemon.NativeCommand(["server", "ws://127.0.0.1:12345", "--restrict-config", "/tmp/rules.yml"]));
    }
}
