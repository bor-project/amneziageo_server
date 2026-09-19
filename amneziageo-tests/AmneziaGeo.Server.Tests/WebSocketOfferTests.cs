using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Hello;
using AmneziaGeo.Server.Api.Proxy;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class WebSocketOfferTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-front-");

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void TheFrontOpenToEverySourceIsOfferedUnderTheNameOfTheEndpoint()
    {
        var panel = Pair("panel", names => names.AddDnsName("vpn.example"));
        var proxies = new[]
        {
            Proxy(1, "relay") with { Kind = ProxyKind.Wg, Path = string.Empty, Target = "127.0.0.1:51821" },
            Proxy(2, "off") with { IsEnabled = false },
            Proxy(3, "office") with { Sources = ["198.51.100.0/24"] },
            Proxy(4, "open") with { Port = 8443, Path = "/secret_path/" },
        };

        var front = WebSocketOffer.Front(proxies, Endpoint("vpn.example"), panel, NullLogger.Instance);

        Assert.Equal(new WebSocketFeature("vpn.example", 8443, "secret_path", 51821), front);
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

        Assert.Equal("vpn.example", WebSocketOffer.Front([Proxy(1, "open")], Endpoint("203.0.113.7"), named, NullLogger.Instance)?.Host);
        Assert.Equal("203.0.113.7", WebSocketOffer.Front([Proxy(1, "open")], Endpoint("203.0.113.7"), addressed, NullLogger.Instance)?.Host);
    }

    [Fact]
    public void AFrontWithNoCertificateIsNotOffered()
    {
        var own = Pair("own", names => names.AddDnsName("front.example"));
        var proxy = Proxy(1, "open");

        Assert.Null(WebSocketOffer.Front([proxy], Endpoint("vpn.example"), new ProxyCertificate(string.Empty, string.Empty), NullLogger.Instance));
        Assert.Equal(
            "front.example",
            WebSocketOffer.Front(
                [proxy with { Certificate = own.Chain, CertificateKey = own.Key }],
                Endpoint("vpn.example"),
                new ProxyCertificate(string.Empty, string.Empty),
                NullLogger.Instance)?.Host);
    }

    [Fact]
    public void ACertificateThatDoesNotReadKeepsTheHostOfTheEndpoint()
    {
        var gone = Path.Combine(_folder.FullName, "gone.pem");

        var front = WebSocketOffer.Front([Proxy(1, "open")], Endpoint("vpn.example"), new ProxyCertificate(gone, gone), NullLogger.Instance);

        Assert.Equal("vpn.example", front?.Host);
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
