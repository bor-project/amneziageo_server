using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Proxy;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class WebSocketFrontsTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-front-");

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void TheFrontOpenToEverySourceGoesUnderTheNameOfTheEndpoint()
    {
        var panel = Pair("panel", names => names.AddDnsName("vpn.example"));
        var proxies = new[]
        {
            Proxy(1, "relay") with { Kind = ProxyKind.Wg, Path = string.Empty, Target = "127.0.0.1:51821" },
            Proxy(2, "off") with { IsEnabled = false },
            Proxy(3, "office") with { Sources = ["198.51.100.0/24"] },
            Proxy(4, "open") with { Port = 8443, Path = "/secret_path/" },
        };

        var front = WebSocketFronts.Front(proxies, Endpoint("vpn.example"), panel, NullLogger.Instance);

        Assert.Equal("wss://vpn.example:8443/secret_path", front);
    }

    [Fact]
    public void AnEndpointTheCertificateDoesNotHoldGivesWayToTheNameItCarries()
    {
        var named = Pair("named", names =>
        {
            names.AddDnsName("*.example");
            names.AddDnsName("vpn.example");
        });
        var addressed = Pair("addressed", names => names.AddIpAddress(IPAddress.Parse("203.0.113.7")));

        Assert.Equal("wss://vpn.example:443/path1", WebSocketFronts.Front([Proxy(1, "open")], Endpoint("203.0.113.7"), named, NullLogger.Instance));
        Assert.Equal("wss://203.0.113.7:443/path1", WebSocketFronts.Front([Proxy(1, "open")], Endpoint("203.0.113.7"), addressed, NullLogger.Instance));
    }

    [Fact]
    public void AFrontWithNoCertificateIsNotNamed()
    {
        var own = Pair("own", names => names.AddDnsName("front.example"));
        var proxy = Proxy(1, "open");

        Assert.Empty(WebSocketFronts.Front([proxy], Endpoint("vpn.example"), new ProxyCertificate(string.Empty, string.Empty), NullLogger.Instance));
        Assert.Equal(
            "wss://front.example:443/path1",
            WebSocketFronts.Front(
                [proxy with { Certificate = own.Chain, CertificateKey = own.Key }],
                Endpoint("vpn.example"),
                new ProxyCertificate(string.Empty, string.Empty),
                NullLogger.Instance));
    }

    [Fact]
    public void ACertificateThatDoesNotReadKeepsTheHostOfTheEndpoint()
    {
        var gone = Path.Combine(_folder.FullName, "gone.pem");

        var front = WebSocketFronts.Front([Proxy(1, "open")], Endpoint("vpn.example"), new ProxyCertificate(gone, gone), NullLogger.Instance);

        Assert.Equal("wss://vpn.example:443/path1", front);
    }

    [Fact]
    public void AnEndpointWithNoHostAndACertificateWithNoNameNamesNoFront()
    {
        var gone = Path.Combine(_folder.FullName, "gone.pem");

        Assert.Empty(WebSocketFronts.Front([Proxy(1, "open")], Endpoint(string.Empty), new ProxyCertificate(gone, gone), NullLogger.Instance));
    }

    [Fact]
    public void AnAddressOfVersionSixGoesInBrackets()
    {
        Assert.Equal("wss://[2001:db8::1]:8080/secret", WebSocketFronts.Address("2001:db8::1", 8080, "secret"));
        Assert.Equal("wss://vpn.example:8080/secret", WebSocketFronts.Address("vpn.example", 8080, "secret"));
    }

    private static ProxyConfig Proxy(long id, string name) => new()
    {
        Id = id,
        Name = name,
        Kind = ProxyKind.Ws,
        IsEnabled = true,
        Port = 443,
        Path = "path" + id,
    };

    private static ServerConfig Endpoint(string host) => new()
    {
        Name = "awg1",
        Host = host,
        ListenPort = 51821,
    };

    private ProxyCertificate Pair(string name, Action<SubjectAlternativeNameBuilder> names)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, ecdsa, HashAlgorithmName.SHA256);
        var alternative = new SubjectAlternativeNameBuilder();
        names(alternative);
        request.CertificateExtensions.Add(alternative.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        return new ProxyCertificate(
            Write(name + ".pem", certificate.ExportCertificatePem()),
            Write(name + ".key", ecdsa.ExportPkcs8PrivateKeyPem()));
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder.FullName, name);
        File.WriteAllText(path, text);

        return path;
    }
}
