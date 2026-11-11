using AmneziaGeo.Server.Api.Dns;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ServiceTrustTests
{
    [Fact]
    public void SeparateSubscriptionCertificateIsUsedOnlyOnItsActualServicePort()
    {
        using var panelIdentity = new ProxyTlsFixture("panel.example.net");
        using var serviceIdentity = new ProxyTlsFixture("services.example.net");
        var panel = PanelDefaults.Settings with { Port = 443, Certificate = panelIdentity.Chain, CertificateKey = panelIdentity.Key };
        var subscription = SubscriptionDefaults.Settings with
        {
            IsEnabled = true, Separate = true, Port = 8443, Certificate = serviceIdentity.Chain, CertificateKey = serviceIdentity.Key,
        };
        var endpoint = ConfigDefaults.Fresh("vpn") with { Host = "services.example.net", ListenPort = 51820, ServicesPort = 8443 };
        var identity = ServiceTrust.Of(endpoint, new WebOptions(), panel, subscription: subscription);
        Assert.Equal("services.example.net", identity!.Name);
        Assert.Equal(serviceIdentity.Pin.ToLowerInvariant(), identity.Pin);
        var shared = ServiceTrust.Of(endpoint with { ServicesPort = 443 }, new WebOptions(), panel, subscription: subscription with { Port = 443 });
        Assert.Equal("panel.example.net", shared!.Name);
        Assert.Equal(panelIdentity.Pin.ToLowerInvariant(), shared.Pin);
        Assert.Equal("services.example.net", endpoint.Host);
        Assert.Equal(8443, endpoint.ServicesPort);
    }

    [Fact]
    public void AuthoritativeListenerFilesTakePrecedenceOverPendingPanelSettings()
    {
        using var current = new ProxyTlsFixture("current.example.net");
        using var pending = new ProxyTlsFixture("pending.example.net");
        var panel = PanelDefaults.Settings with { Certificate = pending.Chain, CertificateKey = pending.Key };
        var endpoint = ConfigDefaults.Fresh("vpn") with { Host = "current.example.net" };
        var identity = ServiceTrust.Of(endpoint, new WebOptions(), panel,
            selectedFiles: new ServiceCertificateFiles(current.Chain, current.Key));
        Assert.Equal("current.example.net", identity!.Name);
        Assert.Equal(current.Pin.ToLowerInvariant(), identity.Pin);
        Assert.Null(ServiceTrust.Of(endpoint, new WebOptions(), panel, selectedFiles: new ServiceCertificateFiles("", "")));
    }

    [Fact]
    public void ProxySniIdentityOverridesThePanelAndSubscriptionChains()
    {
        using var panelIdentity = new ProxyTlsFixture("front.example.net");
        using var proxyIdentity = new ProxyTlsFixture("front.example.net");
        using var subscriptionIdentity = new ProxyTlsFixture("front.example.net");
        var panel = PanelDefaults.Settings with { Port = 443, Certificate = panelIdentity.Chain, CertificateKey = panelIdentity.Key };
        var proxy = proxyIdentity.Proxy();
        var selector = new ProxyCertificates();
        selector.Update([proxy]);
        var endpoint = ConfigDefaults.Fresh("vpn") with { Host = proxy.Host, ServicesPort = proxy.Port };
        var subscription = SubscriptionDefaults.Settings with
        {
            IsEnabled = true, Separate = true, Port = proxy.Port, Certificate = subscriptionIdentity.Chain, CertificateKey = subscriptionIdentity.Key,
        };
        var identity = ServiceTrust.Of(endpoint, new WebOptions(), panel, subscription: subscription, proxies: selector, configured: [proxy]);
        Assert.Equal(proxy.Host, identity!.Name);
        Assert.Equal(proxyIdentity.Pin.ToLowerInvariant(), identity.Pin);
    }

    [Fact]
    public void FallbackSanNameIsResolvedThroughTheActualSelectorAgain()
    {
        using var panelIdentity = new ProxyTlsFixture("hello.example.net");
        using var proxyIdentity = new ProxyTlsFixture("hello.example.net");
        var panel = PanelDefaults.Settings with { Port = 443, Certificate = panelIdentity.Chain, CertificateKey = panelIdentity.Key };
        var proxy = proxyIdentity.Proxy();
        var selector = new ProxyCertificates();
        selector.Update([proxy]);
        var endpoint = ConfigDefaults.Fresh("vpn") with { Host = "outside.example.net", ServicesPort = proxy.Port };
        var identity = ServiceTrust.Of(endpoint, new WebOptions(), panel, proxies: selector, configured: [proxy]);
        Assert.Equal("hello.example.net", identity!.Name);
        Assert.Equal(proxyIdentity.Pin.ToLowerInvariant(), identity.Pin);
    }

    [Fact]
    public void ExplicitIpIdentitySupportsMissingSniWithoutAnArbitraryDefault()
    {
        using var first = new ProxyTlsFixture("127.0.0.1");
        using var second = new ProxyTlsFixture("127.0.0.2");
        var proxy = first.Proxy();
        var other = second.Proxy();
        other.Path = "other";
        var selector = new ProxyCertificates();
        selector.Update([proxy]);
        var endpoint = ConfigDefaults.Fresh("vpn") with { Host = "127.0.0.1", ServicesPort = proxy.Port };
        var panel = PanelDefaults.Settings with { Port = 443, Certificate = "", CertificateKey = "" };
        var identity = ServiceTrust.Of(endpoint, new WebOptions(), panel, proxies: selector, configured: [proxy]);
        Assert.Equal("127.0.0.1", identity!.Name);
        Assert.Equal(first.Pin.ToLowerInvariant(), identity.Pin);
        selector.Update([proxy, other]);
        Assert.Null(ServiceTrust.Of(endpoint, new WebOptions(), panel, proxies: selector, configured: [proxy, other]));
    }

    [Fact]
    public void AProxyOnlyPortExportsItsUniqueConfiguredSanAsTheTlsName()
    {
        using var fixture = new ProxyTlsFixture("hello.example.net");
        using var other = new ProxyTlsFixture("other.example.net");
        var proxy = fixture.Proxy();
        var selector = new ProxyCertificates();
        selector.Update([proxy]);
        var endpoint = ConfigDefaults.Fresh("vpn") with { Host = "origin.example.net", ServicesPort = proxy.Port };
        var panel = PanelDefaults.Settings with { Certificate = "", CertificateKey = "" };
        var identity = ServiceTrust.Of(endpoint, new WebOptions(), panel, proxies: selector, configured: [proxy]);
        Assert.Equal("hello.example.net", identity!.Name);
        Assert.Equal(fixture.Pin.ToLowerInvariant(), identity.Pin);
        Assert.Equal("origin.example.net", endpoint.Host);
        var second = other.Proxy();
        second.Path = "other";
        selector.Update([proxy, second]);
        Assert.Null(ServiceTrust.Of(endpoint, new WebOptions(), panel, proxies: selector, configured: [proxy, second]));
    }

    [Fact]
    public void MissingHostUsesTheExportHostWithoutChangingTheEndpoint()
    {
        using var fixture = new ProxyTlsFixture();
        var panel = PanelDefaults.Settings with { Certificate = fixture.Chain, CertificateKey = fixture.Key };
        var endpoint = ConfigDefaults.Fresh("vpn") with { Host = "" };
        var identity = ServiceTrust.Of(endpoint, new WebOptions(), panel, publicName: "proxy.example.net");
        Assert.Equal("proxy.example.net", identity!.Name);
        Assert.Equal(fixture.Pin.ToLowerInvariant(), identity.Pin);
        Assert.Empty(endpoint.Host);
    }

    [Fact]
    public void BrokenOrExpiredConfiguredIdentityDoesNotProduceTrustComments()
    {
        using var fixture = new ProxyTlsFixture();
        var panel = PanelDefaults.Settings with { Certificate = fixture.Chain, CertificateKey = fixture.Key };
        var endpoint = ConfigDefaults.Fresh("vpn") with { Host = "proxy.example.net" };
        File.WriteAllText(fixture.Key, "invalid-key");
        Assert.Null(ServiceTrust.Of(endpoint, new WebOptions(), panel));
        using var expired = new ProxyTlsFixture(before: DateTimeOffset.UtcNow.AddDays(-3), after: DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Null(ServiceTrust.Of(endpoint, new WebOptions(), panel with { Certificate = expired.Chain, CertificateKey = expired.Key }));
    }

    [Fact]
    public void TrustedCaLeavesThePinEmptyAcrossCertificateRenewal()
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=fixture-root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));
        using var first = Leaf();
        using var renewed = Leaf();
        var certificates = new X509Certificate2Collection(root);
        var roots = new X509Certificate2Collection(root);
        Assert.NotEqual(first.Thumbprint, renewed.Thumbprint);
        foreach (var leaf in new[] { first, renewed })
        {
            var identity = ServiceTrust.Of(leaf, certificates, "hello.example.net", roots);
            Assert.Equal("hello.example.net", identity!.Name);
            Assert.Empty(identity.Pin);
            Assert.Null(ServiceTrust.Of(leaf, certificates, "wrong.example.net", roots));
        }

        X509Certificate2 Leaf()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=hello.example.net", key, HashAlgorithmName.SHA256);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("hello.example.net");
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            return request.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
        }
    }

    [Fact]
    public async Task SubscriptionFeedExportsTheSharedServiceIdentity()
    {
        using var bench = new Bench();
        using var panelIdentity = new ProxyTlsFixture("panel.example.net");
        using var servicesIdentity = new ProxyTlsFixture("services.example.net");
        var panel = PanelDefaults.Settings with { Port = 443, Certificate = panelIdentity.Chain, CertificateKey = panelIdentity.Key };
        await bench.Panel.SeedAsync(panel, default).ConfigureAwait(false);
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("vpn") with
        {
            Host = "services.example.net", ServicesPort = 8443,
        }, default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "peer"), default).ConfigureAwait(false)).Record!;
        var subscriptions = new SubscriptionState
        {
            Current = SubscriptionDefaults.Settings with
            {
                IsEnabled = true, Separate = true, Port = 8443, Certificate = servicesIdentity.Chain, CertificateKey = servicesIdentity.Key,
            },
        };
        var feed = new SubscriptionFeed(bench.Clients, bench.Configs, bench.Templates,
            bench.Services.GetRequiredService<TrafficLedger>(), bench.Resolver, bench.Services.GetRequiredService<DnsState>(),
            bench.Services.GetRequiredService<DnsHolders>(), bench.Panel, subscriptions: subscriptions, runningPanel: panel);
        var result = await feed.ReadAsync(client.SubscriptionId, "panel.example.net", default).ConfigureAwait(false);
        var sheet = Assert.Single(result!.Sheets);
        Assert.Contains("# AmneziaGeo Services TLS Name = services.example.net", sheet.Text);
        Assert.Contains("# AmneziaGeo Services TLS Pin = " + servicesIdentity.Pin.ToLowerInvariant(), sheet.Text);
        Assert.DoesNotContain(panelIdentity.Pin.ToLowerInvariant(), sheet.Text);
        Assert.Equal(endpoint.Id, (await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!.Id);
        Assert.Equal(endpoint.PrivateKey, (await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!.PrivateKey);
    }
}
