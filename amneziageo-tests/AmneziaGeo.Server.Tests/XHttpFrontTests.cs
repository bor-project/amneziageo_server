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

        using var panel = await front.SendAsync(HttpMethod.Get, "/api/probe", forwardedHttps: forwardedHttps).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, panel.StatusCode);

        using var hello = await front.SendAsync(HttpMethod.Post, ServiceDesk.HelloPath, forwardedHttps: forwardedHttps,
            content: new StringContent("{}")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, hello.StatusCode);

        using var after = await front.SendAsync(HttpMethod.Get, "/api/probe", secure: true).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal("tls", await after.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(0, front.WebSocketStarts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlainXhttpOriginReturnsControlledProtocolErrors(bool forwardedHttps)
    {
        var front = new Front();
        await using var disposing = front.ConfigureAwait(false);
        await front.StartAsync().ConfigureAwait(false);

        using var post = await front.SendAsync(HttpMethod.Post, "/relay/session/0", forwardedHttps: forwardedHttps).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.Contains("GET", post.Content.Headers.Allow);

        using var body = await front.SendAsync(HttpMethod.Get, "/relay/session/0", forwardedHttps: forwardedHttps,
            content: new StringContent("body")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, body.StatusCode);

        using var get = await front.SendAsync(HttpMethod.Get, "/relay/session/0", forwardedHttps: forwardedHttps).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, get.StatusCode);

        using var other = await front.SendAsync(HttpMethod.Get, "/unrelated", forwardedHttps: forwardedHttps).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);

        using var secure = await front.SendAsync(HttpMethod.Get, "/api/probe", secure: true).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, secure.StatusCode);
        Assert.Equal(0, front.WebSocketStarts);
    }

    private sealed class Front : IAsyncDisposable
    {
        private readonly Bench _bench = new();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "amneziageo-xhttp-front-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient _client = new(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            SslOptions = new()
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        }) { Timeout = TimeSpan.FromSeconds(5) };
        private readonly Fronts _fronts = new();
        private readonly int _port = Free();
        private WebApplication? _app;

        public int WebSocketStarts => _fronts.Starts;

        public async Task StartAsync()
        {
            Directory.CreateDirectory(_directory);
            var certificate = Certificate(_directory);
            var panel = PanelDefaults.Settings with
            {
                Listen = ["127.0.0.1"],
                Port = _port,
                Path = "",
                Certificate = certificate.Chain,
                CertificateKey = certificate.Key,
            };
            await _bench.Panel.SeedAsync(panel, default).ConfigureAwait(false);
            await new TransportProxyStore(_bench.Db).SaveAsync(0, new TransportProxyEntity
            {
                Name = "XHTTP origin",
                Transport = "xhttp", ProtocolVersion = 2,
                Host = "cdn.example.net",
                Port = 443,
                ListenPort = _port,
                OriginHttp = true,
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
            Assert.True(app.Services.GetRequiredService<PanelPlace>().Secure);
            var point = Assert.IsType<ServicePoint>(app.Services.GetRequiredService<ServiceShare>().Point);
            Assert.Equal(_port, point.Port);
            Assert.Contains(point.Endpoints, endpoint => endpoint.XHttp && endpoint.OriginHttp && endpoint.Path == "relay");
            Assert.True(app.Services.GetRequiredService<ProxyCertificates>().AllowsHttp(_port));
        }

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, bool secure = false,
            bool forwardedHttps = false, HttpContent? content = null)
        {
            using var request = new HttpRequestMessage(method, $"{(secure ? "https" : "http")}://127.0.0.1:{_port}{path}")
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

        private static (string Chain, string Key) Certificate(string directory)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var chain = Path.Combine(directory, "front.pem");
            var privateKey = Path.Combine(directory, "front.key");
            File.WriteAllText(chain, certificate.ExportCertificatePem());
            File.WriteAllText(privateKey, key.ExportPkcs8PrivateKeyPem());
            return (chain, privateKey);
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
