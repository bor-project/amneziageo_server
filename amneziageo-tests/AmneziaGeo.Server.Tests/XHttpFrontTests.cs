using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.ClientUpdates;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Proxy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using ServicePoint = AmneziaGeo.Server.Api.Services.ServicePoint;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class XHttpFrontTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedPortKeepsPanelAndHelloBehindActualTls(bool forwardedHttps)
    {
        var front = new Front();
        await using var disposing = front.ConfigureAwait(false);
        await front.StartAsync().ConfigureAwait(false);

        using var secure = await front.SendAsync(HttpMethod.Get, "/api/probe", secure: true).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, secure.StatusCode);
        Assert.Equal("tls", await secure.Content.ReadAsStringAsync().ConfigureAwait(false));

        await front.AssertPlainRejectedAsync(HttpMethod.Get, "/api/probe", forwardedHttps).ConfigureAwait(false);
        await front.AssertPlainRejectedAsync(HttpMethod.Post, ServiceDesk.HelloPath, forwardedHttps).ConfigureAwait(false);

        using var after = await front.SendAsync(HttpMethod.Get, "/api/probe", secure: true).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal("tls", await after.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(0, front.WebSocketStarts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlainProxyIsRejectedWithoutDisturbingTlsPanel(bool forwardedHttps)
    {
        var front = new Front();
        await using var disposing = front.ConfigureAwait(false);
        await front.StartAsync().ConfigureAwait(false);
        foreach (var path in new[] { "/relay/session/0", "/unrelated" })
        {
            await front.AssertPlainRejectedAsync(HttpMethod.Get, path, forwardedHttps).ConfigureAwait(false);
            await front.AssertPlainRejectedAsync(HttpMethod.Post, path, forwardedHttps).ConfigureAwait(false);
        }

        using var secure = await front.SendAsync(HttpMethod.Get, "/api/probe", secure: true).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, secure.StatusCode);
        Assert.Equal("tls", await secure.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(0, front.WebSocketStarts);
    }

    [Fact]
    public async Task TlsAnonymousFailuresMatchUnknownPathWithoutBackend()
    {
        var front = new Front();
        await using var disposing = front.ConfigureAwait(false);
        await front.StartAsync().ConfigureAwait(false);
        foreach (var path in new[] { "/unrelated", "/relay/?op=challenge", "/relay/?op=unknown" })
        {
            using var response = await front.SendAsync(HttpMethod.Get, path, secure: true).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
            Assert.DoesNotContain(response.Headers, header => header.Key.StartsWith("X-WG-", StringComparison.OrdinalIgnoreCase));
            Assert.False(response.Content.Headers.Contains("Content-Type"));
            Assert.False(response.Headers.Contains("X-Accel-Buffering"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlainPanelKeepsHttpWhileProxyUsesItsOwnTlsIdentity(bool forwardedHttps)
    {
        var front = new Front(panelTls: false);
        await using var disposing = front.ConfigureAwait(false);
        await front.StartAsync().ConfigureAwait(false);
        using var panel = await front.SendAsync(HttpMethod.Get, "/api/probe", forwardedHttps: forwardedHttps).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        Assert.Equal("plain", await panel.Content.ReadAsStringAsync().ConfigureAwait(false));
        await front.AssertPlainRejectedAsync(HttpMethod.Get, "/relay/?op=challenge", forwardedHttps).ConfigureAwait(false);
        using var secure = await front.SendAsync(HttpMethod.Get, "/api/probe", secure: true).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, secure.StatusCode);
        Assert.Equal("tls", await secure.Content.ReadAsStringAsync().ConfigureAwait(false));
        using var proxy = await front.SendAsync(HttpMethod.Get, "/relay/?op=challenge", secure: true).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, proxy.StatusCode);
        Assert.Empty(await proxy.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
        using var after = await front.SendAsync(HttpMethod.Get, "/api/probe").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task PlainPanelAndIpProxySharePortWithoutSniOrAMadeUpCertificate()
    {
        var front = new Front(panelTls: false, ipProxy: true);
        await using var disposing = front.ConfigureAwait(false);
        await front.StartAsync().ConfigureAwait(false);
        var identity = await front.TrustAsync().ConfigureAwait(false);
        Assert.Equal("127.0.0.1", identity!.Name);
        Assert.Equal(front.CertificatePin.ToLowerInvariant(), identity.Pin);
        using var panel = await front.SendAsync(HttpMethod.Get, "/api/probe").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        Assert.Equal("plain", await panel.Content.ReadAsStringAsync().ConfigureAwait(false));
        using var secure = await front.SendAsync(HttpMethod.Get, "/api/probe", secure: true).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, secure.StatusCode);
        Assert.Equal("tls", await secure.Content.ReadAsStringAsync().ConfigureAwait(false));
        using var proxy = await front.SendAsync(HttpMethod.Get, "/relay/?op=challenge", secure: true).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, proxy.StatusCode);
        Assert.Empty(await proxy.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
        await front.AssertPlainRejectedAsync(HttpMethod.Get, "/relay/?op=challenge", forwardedHttps: true).ConfigureAwait(false);
        using var after = await front.SendAsync(HttpMethod.Get, "/api/probe").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    private sealed class Front : IAsyncDisposable
    {
        private readonly Bench _bench = new();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "amneziageo-xhttp-front-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient _client;
        private string _pin = "";
        private readonly bool _panelTls;
        private readonly bool _ipProxy;

        /// <summary>
        /// ctor
        /// </summary>
        public Front(bool panelTls = true, bool ipProxy = false)
        {
            _panelTls = panelTls;
            _ipProxy = ipProxy;
            _client = new HttpClient(new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                SslOptions = new()
                {
                    RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is X509Certificate2 leaf
                        && leaf.MatchesHostname("127.0.0.1", allowCommonName: false)
                        && Convert.ToHexString(SHA256.HashData(leaf.RawData)) == _pin
                        && DateTime.UtcNow >= leaf.NotBefore.ToUniversalTime()
                        && DateTime.UtcNow <= leaf.NotAfter.ToUniversalTime(),
                },
            }) { Timeout = TimeSpan.FromSeconds(10) };
        }

        private readonly Fronts _fronts = new();
        private readonly int _port = Free();
        private WebApplication? _app;

        public int WebSocketStarts => _fronts.Starts;

        public string CertificatePin => _pin;

        public async Task<AmneziaGeo.Server.Awg.Client.ServiceTlsIdentity?> TrustAsync()
        {
            var app = _app!;
            return ServiceTrust.Of(AmneziaGeo.Server.Awg.Config.ConfigDefaults.Fresh("services") with
                { Host = _ipProxy ? "127.0.0.1" : "localhost", ServicesPort = _port },
                app.Services.GetRequiredService<WebOptions>(), app.Services.GetRequiredService<PanelSettings>(),
                subscription: app.Services.GetRequiredService<SubscriptionState>().Current,
                proxies: app.Services.GetRequiredService<ProxyCertificates>(),
                configured: await new TransportProxyStore(_bench.Db).ListAsync(default).ConfigureAwait(false),
                selectedFiles: await app.Services.GetRequiredService<ServiceServer>().CertificateFilesAsync(_port, default).ConfigureAwait(false));
        }



        public async Task StartAsync()
        {
            Directory.CreateDirectory(_directory);
            var certificate = Certificate(_directory);
            _pin = certificate.Pin;
            var panel = PanelDefaults.Settings with
            {
                Listen = ["127.0.0.1"],
                Port = _port,
                Path = "",
                Certificate = _panelTls ? certificate.Chain : "",
                CertificateKey = _panelTls ? certificate.Key : "",
            };
            await _bench.Panel.SeedAsync(panel, default).ConfigureAwait(false);
            await new TransportProxyStore(_bench.Db).SaveAsync(0, new TransportProxyEntity
            {
                Name = "TLS HTTP",
                Transport = "xhttp", ProtocolVersion = 2,
                Host = _ipProxy ? "127.0.0.1" : "localhost",
                Port = 443,
                ListenPort = _port,
                OriginHttp = false,
                Certificate = certificate.Chain, CertificateKey = certificate.Key, Pin = certificate.Pin,
                Path = "relay",
                IsEnabled = true,
            }, default).ConfigureAwait(false);

            var builder = WebApplication.CreateSlimBuilder();
            builder.Configuration["Database:Path"] = _bench.DatabasePath;
            builder.Configuration["Proxy:Directory"] = _directory;
            builder.Configuration["XHttp:Executable"] = Path.Combine(_directory, "missing-get");
            builder.Configuration["Web:Proxies:0"] = "127.0.0.1";
            builder.AddListening();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddServerDatabase(_bench.DatabasePath, new AuthOptions());
            builder.Services.AddSingleton(new DnsState());
            builder.Services.AddSingleton<IHostNetwork>(new Ledger());
            builder.Services.AddSingleton<IHostCommands>(new Tools());
            builder.Services.AddFirewall();
            builder.Services.AddEndpointServices(builder.Configuration);
            builder.Services.AddSingleton<IProxyRunner>(_fronts);
            builder.Services.AddSubscriptions();
            builder.Services.AddClientUpdates();

            var app = builder.Build();
            _app = app;
            app.UseForwarded();
            app.UseEndpointServices();
            app.MapGet("/api/probe", (HttpContext context) => Results.Text(
                context.Features.Get<ITlsConnectionFeature>() is null ? "plain" : "tls"));
            await app.StartAsync().ConfigureAwait(false);
            Assert.Equal(_panelTls, app.Services.GetRequiredService<PanelPlace>().Secure);
            var point = Assert.IsType<ServicePoint>(app.Services.GetRequiredService<ServiceShare>().Point);
            Assert.Equal(_port, point.Port);
            Assert.Contains(point.Endpoints, endpoint => endpoint.XHttp && !endpoint.OriginHttp && endpoint.Path == "relay");
            Assert.False(app.Services.GetRequiredService<ProxyCertificates>().AllowsHttp(_port));
        }

        public async Task AssertPlainRejectedAsync(HttpMethod method, string path, bool forwardedHttps)
        {
            try
            {
                using var response = await SendAsync(method, path, forwardedHttps: forwardedHttps).ConfigureAwait(false);
                Assert.False(response.IsSuccessStatusCode);
                Assert.DoesNotContain(response.Headers, header => header.Key.StartsWith("X-WG-", StringComparison.OrdinalIgnoreCase));
                Assert.False((int)response.StatusCode is >= 300 and < 400);
            }
            catch (HttpRequestException)
            {
            }
        }

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, bool secure = false,
            bool forwardedHttps = false, HttpContent? content = null)
        {
            var host = secure && !_panelTls && !_ipProxy ? "localhost" : "127.0.0.1";
            using var request = new HttpRequestMessage(method, $"{(secure ? "https" : "http")}://{host}:{_port}{path}")
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = content,
            };
            if (forwardedHttps)
            {
                request.Headers.Add("X-Forwarded-Proto", "https");
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await _client.SendAsync(request, deadline.Token).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            try
            {
                if (_app is { } app)
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await app.StopAsync(deadline.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        await app.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _bench.Dispose();
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
            }
        }

        private static (string Chain, string Key, string Pin) Certificate(string directory)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var chain = Path.Combine(directory, "front.pem");
            var privateKey = Path.Combine(directory, "front.key");
            File.WriteAllText(chain, certificate.ExportCertificatePem());
            File.WriteAllText(privateKey, key.ExportPkcs8PrivateKeyPem());
            return (chain, privateKey, Convert.ToHexString(SHA256.HashData(certificate.RawData)));
        }

        private static int Free()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    private sealed class Fronts : IProxyRunner
    {
        public int Starts { get; private set; }

        public Task<ProxyState> EnableAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Down);

        public Task<ProxyState> StartAsync(string name, CancellationToken ct)
        {
            Starts++;
            return Task.FromResult(ProxyState.Down);
        }

        public Task<ProxyState> StopAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Down);

        public Task<ProxyState> StateAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Down);
    }
}
