using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Proxy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

#pragma warning disable xUnit1030
public partial class ServiceTests
{
    [Fact]
    public async Task SharedSubscriptionPortServesTlsHelloAndBothWebSocketPaths()
    {
        await using var fixture = await SharedPortFixture.CreateAsync().ConfigureAwait(false);
        var (endpoint, client) = await fixture.EndpointAsync().ConfigureAwait(false);
        var proxy = await fixture.ProxyAsync("relay").ConfigureAwait(false);
        await fixture.AuthorizeAsync(proxy, client).ConfigureAwait(false);
        Assert.Empty(await fixture.Services.SettleAsync(default).ConfigureAwait(false));

        await fixture.SaveSubscriptionsAsync(fixture.Port, "sub", HttpStatusCode.OK).ConfigureAwait(false);

        await fixture.SubscriptionAsync(fixture.Port, "sub", client.SubscriptionId).ConfigureAwait(false);
        await fixture.HelloAsync(fixture.Port, endpoint, client).ConfigureAwait(false);
        await fixture.EchoAsync("legacy", "awg1").ConfigureAwait(false);
        await fixture.EchoAsync("relay", "transport-proxy-" + proxy.Id, TransportProxyStore.Credential(proxy, client.Id)).ConfigureAwait(false);
    }

    [Fact]
    public async Task SharedSubscriptionPortHandsTheListenerToEndpointsAndBack()
    {
        await using var fixture = await SharedPortFixture.CreateAsync().ConfigureAwait(false);
        await fixture.SaveSubscriptionsAsync(fixture.Port, "sub", HttpStatusCode.OK).ConfigureAwait(false);
        var (endpoint, client) = await fixture.EndpointAsync().ConfigureAwait(false);

        Assert.Empty(await fixture.Services.SettleAsync(default).ConfigureAwait(false));
        Assert.Empty(fixture.Services.Refused);
        await fixture.SubscriptionAsync(fixture.Port, "sub", client.SubscriptionId).ConfigureAwait(false);
        await fixture.EchoAsync("legacy", "awg1").ConfigureAwait(false);

        var movedPort = Free();
        await fixture.Bench.Configs.ChangeAsync(endpoint.Id, endpoint with { ListenPort = movedPort, ServicesPort = movedPort }, default).ConfigureAwait(false);
        Assert.Empty(await fixture.Services.SettleAsync(default).ConfigureAwait(false));
        await fixture.SubscriptionAsync(fixture.Port, "sub", client.SubscriptionId).ConfigureAwait(false);
        using var answer = await fixture.Tls.PostAsync($"https://localhost:{fixture.Port}/api/hello", new StringContent("{}", Encoding.UTF8, "application/json")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);

        await fixture.Bench.Configs.ChangeAsync(endpoint.Id, endpoint, default).ConfigureAwait(false);
        Assert.Empty(await fixture.Services.SettleAsync(default).ConfigureAwait(false));
        await fixture.SubscriptionAsync(fixture.Port, "sub", client.SubscriptionId).ConfigureAwait(false);
        await fixture.EchoAsync("legacy", "awg1").ConfigureAwait(false);
    }

    [Fact]
    public async Task SharedSubscriptionPortSurvivesPathPortAndHostedServiceRestart()
    {
        await using var fixture = await SharedPortFixture.CreateAsync().ConfigureAwait(false);
        var (endpoint, client) = await fixture.EndpointAsync().ConfigureAwait(false);
        Assert.Empty(await fixture.Services.SettleAsync(default).ConfigureAwait(false));
        await fixture.SaveSubscriptionsAsync(fixture.Port, "sub", HttpStatusCode.OK).ConfigureAwait(false);
        await fixture.SaveSubscriptionsAsync(fixture.Port, "renamed-sub", HttpStatusCode.OK).ConfigureAwait(false);
        using var former = await fixture.Tls.GetAsync($"https://localhost:{fixture.Port}/sub/{client.SubscriptionId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, former.StatusCode);
        await fixture.SubscriptionAsync(fixture.Port, "renamed-sub", client.SubscriptionId).ConfigureAwait(false);

        var otherPort = Free();
        await fixture.SaveSubscriptionsAsync(otherPort, "renamed-sub", HttpStatusCode.OK).ConfigureAwait(false);
        await fixture.SubscriptionAsync(otherPort, "renamed-sub", client.SubscriptionId).ConfigureAwait(false);
        using var apart = await fixture.Tls.GetAsync($"https://localhost:{fixture.Port}/renamed-sub/{client.SubscriptionId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, apart.StatusCode);
        await fixture.SaveSubscriptionsAsync(fixture.Port, "renamed-sub", HttpStatusCode.OK).ConfigureAwait(false);

        await fixture.Subscriptions.StopAsync(default).ConfigureAwait(false);
        await fixture.Services.StopAsync(default).ConfigureAwait(false);
        await fixture.Services.StartAsync(default).ConfigureAwait(false);
        await fixture.Subscriptions.StartAsync(default).ConfigureAwait(false);

        await fixture.SubscriptionAsync(fixture.Port, "renamed-sub", client.SubscriptionId).ConfigureAwait(false);
        await fixture.HelloAsync(fixture.Port, endpoint, client).ConfigureAwait(false);
        await fixture.EchoAsync("legacy", "awg1").ConfigureAwait(false);
        Assert.False(Listens(otherPort));
    }

    [Theory]
    [InlineData("legacy", "websocket")]
    [InlineData("relay", "websocket")]
    [InlineData("xhttp", "xhttp")]
    public async Task SharedSubscriptionPortRejectsPathCollisionWithoutReplacingTheSubscription(string path, string transport)
    {
        await using var fixture = await SharedPortFixture.CreateAsync().ConfigureAwait(false);
        var (_, client) = await fixture.EndpointAsync().ConfigureAwait(false);
        if (path != "legacy")
        {
            await fixture.ProxyAsync(path, transport, enabled: transport == "websocket").ConfigureAwait(false);
        }

        Assert.Empty(await fixture.Services.SettleAsync(default).ConfigureAwait(false));
        await fixture.SaveSubscriptionsAsync(fixture.Port, "sub", HttpStatusCode.OK).ConfigureAwait(false);

        await fixture.SaveSubscriptionsAsync(fixture.Port, path, HttpStatusCode.BadRequest).ConfigureAwait(false);

        Assert.Equal("sub", fixture.State.Current.Path);
        await fixture.SubscriptionAsync(fixture.Port, "sub", client.SubscriptionId).ConfigureAwait(false);
        await fixture.EchoAsync("legacy", "awg1").ConfigureAwait(false);
    }

    [Fact]
    public async Task SharedSubscriptionPortRecoversItsOwnListenerAfterAServiceSettleException()
    {
        await using var fixture = await SharedPortFixture.CreateAsync().ConfigureAwait(false);
        await fixture.SaveSubscriptionsAsync(fixture.Port, "sub", HttpStatusCode.OK).ConfigureAwait(false);
        var (_, client) = await fixture.EndpointAsync().ConfigureAwait(false);
        fixture.RefuseFrontStart = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Services.SettleAsync(default)).ConfigureAwait(false);

        await fixture.SubscriptionAsync(fixture.Port, "sub", client.SubscriptionId).ConfigureAwait(false);
        fixture.RefuseFrontStart = false;
        Assert.Empty(await fixture.Services.SettleAsync(default).ConfigureAwait(false));
        await fixture.SubscriptionAsync(fixture.Port, "sub", client.SubscriptionId).ConfigureAwait(false);
        await fixture.EchoAsync("legacy", "awg1").ConfigureAwait(false);
    }

    [Fact]
    public async Task SharedSubscriptionPortRecoversAfterARefusedBindAndCanRetryTheHandoff()
    {
        await using var fixture = await SharedPortFixture.CreateAsync().ConfigureAwait(false);
        await fixture.SaveSubscriptionsAsync(fixture.Port, "sub", HttpStatusCode.OK).ConfigureAwait(false);
        var (endpoint, client) = await fixture.EndpointAsync().ConfigureAwait(false);
        fixture.BlockNextServiceBind();

        var faults = await fixture.Services.SettleAsync(default).ConfigureAwait(false);

        Assert.True(faults.ContainsKey(endpoint.Id));
        Assert.True(fixture.Services.Refused.ContainsKey(fixture.Port));
        Assert.False(fixture.Services.ListensOn(fixture.Port));
        await fixture.SubscriptionAsync(fixture.Port, "sub", client.SubscriptionId).ConfigureAwait(false);
        Assert.Empty(await fixture.Services.SettleAsync(default).ConfigureAwait(false));
        await fixture.EchoAsync("legacy", "awg1").ConfigureAwait(false);
    }

    [Fact]
    public async Task SharedSubscriptionPortHandoffDoesNotDeadlockWithConcurrentApply()
    {
        await using var fixture = await SharedPortFixture.CreateAsync().ConfigureAwait(false);
        await fixture.SaveSubscriptionsAsync(fixture.Port, "sub", HttpStatusCode.OK).ConfigureAwait(false);
        var (_, client) = await fixture.EndpointAsync().ConfigureAwait(false);
        var pause = fixture.PauseNextFront();
        var handing = fixture.Services.SettleAsync(default);
        try
        {
            await pause.Entered.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            Assert.Empty(await fixture.Subscriptions.ApplyAsync(fixture.State.Current, default).WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
        }
        finally
        {
            pause.Resume();
        }

        Assert.Empty(await handing.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
        var concurrent = Enumerable.Range(0, 8).Select(index => index % 2 == 0
            ? (Task)fixture.Services.SettleAsync(default)
            : fixture.Subscriptions.ApplyAsync(fixture.State.Current, default)).ToArray();
        await Task.WhenAll(concurrent).WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        await fixture.SubscriptionAsync(fixture.Port, "sub", client.SubscriptionId).ConfigureAwait(false);
        await fixture.EchoAsync("legacy", "awg1").ConfigureAwait(false);
    }

    private sealed class SharedPortFixture : IAsyncDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "ag682-" + Guid.NewGuid().ToString("N"));
        private readonly ScopeOverrides _scopes;
        private readonly EchoFronts _fronts;
        private readonly WebOptions _options;
        private readonly string _thumbprint;
        private TcpListener? _blocker;
        private WebApplication? _api;
        private HttpClient? _control;

        public Bench Bench { get; } = new();
        public int Port { get; } = Free();
        public SubscriptionState State { get; } = new();
        public ServiceServer Services { get; }
        public SubscriptionServer Subscriptions { get; }
        public HttpClient Tls { get; }
        public bool RefuseFrontStart { set => _fronts.Refuse = value; }

        /// <summary>
        /// ctor
        /// </summary>
        private SharedPortFixture()
        {
            Directory.CreateDirectory(_folder);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var chain = Path.Combine(_folder, "chain.pem");
            var privateKey = Path.Combine(_folder, "key.pem");
            File.WriteAllText(chain, certificate.ExportCertificatePem());
            File.WriteAllText(privateKey, key.ExportPkcs8PrivateKeyPem());
            _thumbprint = certificate.Thumbprint;
            _options = new WebOptions { Certificate = chain, CertificateKey = privateKey };
            _scopes = new ScopeOverrides(Bench.Scopes);
            _fronts = new EchoFronts(_folder);
            Subscriptions = new SubscriptionServer(_scopes, State, PanelDefaults.Settings, _options, NullLogger<SubscriptionServer>.Instance);
            _scopes.Subscriptions = Subscriptions;
            var host = new ProxyHost(new Ledger(), _fronts, _folder);
            Services = new ServiceServer(_scopes, Desk(Bench, new SpeedTickets(Bench.Clock), subscriptions: State, fronts: host),
                host, _options, PanelDefaults.Settings,
                new ServiceShare(), new SharedPortLogger(() =>
                {
                    _blocker?.Dispose();
                    _blocker = null;
                }), State);
            Tls = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, received, _, errors) => received?.Thumbprint == _thumbprint
                    && (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0,
            }) { Timeout = TimeSpan.FromSeconds(15) };
        }

        public static async Task<SharedPortFixture> CreateAsync()
        {
            var fixture = new SharedPortFixture();
            await fixture.StartControlAsync().ConfigureAwait(false);
            return fixture;
        }

        public async Task<(ServerConfig Endpoint, TunnelClient Client)> EndpointAsync()
        {
            var endpoint = (await Bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with
            {
                Host = "localhost", ListenPort = Port, ServicesPort = Port, WebSocket = true, WebSocketPath = "legacy",
            }, default).ConfigureAwait(false)).Record!;
            var front = FreeFront();
            var id = front - ConfigServices.FrontBase;
            await Bench.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Configs SET Id={id} WHERE Id={endpoint.Id};").ConfigureAwait(false);
            Bench.Db.ChangeTracker.Clear();
            endpoint = (await Bench.Configs.FindAsync(id, default).ConfigureAwait(false))!;
            var client = (await Bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone"), default).ConfigureAwait(false)).Record!;
            return (endpoint, client);
        }

        public async Task<TransportProxyEntity> ProxyAsync(string path, string transport = "websocket", bool enabled = true) =>
            await new TransportProxyStore(Bench.Db).SaveAsync(0, new TransportProxyEntity
            {
                Name = path, Host = "localhost", Port = Port, Path = path, Transport = transport, IsEnabled = enabled,
            }, default).ConfigureAwait(false);

        public async Task AuthorizeAsync(TransportProxyEntity proxy, TunnelClient client)
        {
            var template = (await Bench.Templates.AddAsync(new ClientTemplate { Name = "proxy-client", ProxyIds = [proxy.Id] }, default).ConfigureAwait(false)).Record!;
            var entity = await Bench.Db.Clients.SingleAsync(one => one.Id == client.Id).ConfigureAwait(false);
            entity.TemplateId = template.Id;
            await Bench.Db.SaveChangesAsync().ConfigureAwait(false);
        }

        public void BlockNextServiceBind()
        {
            _fronts.Starting = () =>
            {
                _fronts.Starting = null;
                _blocker = new TcpListener(IPAddress.Loopback, Port);
                _blocker.Start();
                return Task.CompletedTask;
            };
        }

        public (Task Entered, Action Resume) PauseNextFront()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _fronts.Starting = async () =>
            {
                _fronts.Starting = null;
                entered.TrySetResult();
                await resume.Task.ConfigureAwait(false);
            };
            return (entered.Task, () => resume.TrySetResult());
        }

        public async Task SaveSubscriptionsAsync(int port, string path, HttpStatusCode expected)
        {
            var request = new SubscriptionRequest(true, true, ["127.0.0.1"], [], port, false, path,
                _options.Certificate, _options.CertificateKey, 24, "AG682");
            using var response = await _control!.PutAsJsonAsync("/api/subscription", request).ConfigureAwait(false);
            Assert.Equal(expected, response.StatusCode);
        }

        public async Task SubscriptionAsync(int port, string path, string id)
        {
            using var response = await Tls.GetAsync($"https://localhost:{port}/{path}/{id}").ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-AmneziaGeo-Subscription")));
        }

        public async Task HelloAsync(int port, ServerConfig endpoint, TunnelClient client)
        {
            using var response = await Tls.PostAsJsonAsync($"https://localhost:{port}/api/hello", Token(client.PrivateKey, endpoint.PublicKey, Bench.Clock.GetUtcNow())).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public async Task EchoAsync(string path, string expected, string? secret = null)
        {
            using var socket = new ClientWebSocket();
            socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, errors) => certificate?.GetCertHashString() == _thumbprint
                && (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0;
            if (secret is not null)
            {
                socket.Options.SetRequestHeader("Authorization", "Bearer " + secret);
                socket.Options.SetRequestHeader("X-AmneziaGeo-Target", "127.0.0.1:51820");
            }

            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(new Uri($"wss://localhost:{Port}/{path}/events"), limit.Token).ConfigureAwait(false);
            await socket.SendAsync("payload"u8.ToArray().AsMemory(), WebSocketMessageType.Text, true, limit.Token).ConfigureAwait(false);
            var buffer = new byte[256];
            var read = await socket.ReceiveAsync(buffer.AsMemory(), limit.Token).ConfigureAwait(false);
            Assert.Equal(expected + ":payload", Encoding.UTF8.GetString(buffer, 0, read.Count));
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", limit.Token).ConfigureAwait(false);
        }

        private async Task StartControlAsync()
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            var port = Free();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port));
            builder.Services.AddSingleton<IHostLifetime, SharedPortLifetime>();
            builder.Services.AddSingleton(Bench.Db);
            builder.Services.AddSingleton(Bench.Configs);
            builder.Services.AddSingleton(new SubscriptionStore(Bench.Db, Bench.Clock));
            builder.Services.AddSingleton(State);
            builder.Services.AddSingleton(Subscriptions);
            builder.Services.AddSingleton(Services);
            builder.Services.AddSingleton(PanelDefaults.Settings);
            builder.Services.AddSingleton(_options);
            builder.Services.AddSingleton<ITokenIssuer>(Bench.Issuer);
            builder.Services.AddSingleton<TimeProvider>(Bench.Clock);
            builder.Services.AddSingleton(new FirewallApplier(Bench.Configs, Bench.Panel, new SubscriptionStore(Bench.Db, Bench.Clock),
                Bench.Resolver, new DnsState(), new FirewallHost(new Tools(), new Ledger(), string.Empty), NullLogger<FirewallApplier>.Instance));
            _api = builder.Build();
            _api.UseBearer();
            _api.MapSubscriptions();
            await _api.StartAsync().ConfigureAwait(false);
            _control = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(20) };
            _control.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Bench.Issuer.Issue(
                new Principal(1, "test", AuthScheme.Bearer, 1, new HashSet<string> { Scopes.ManageAccess }), Bench.Clock.GetUtcNow()));
        }

        public async ValueTask DisposeAsync()
        {
            _control?.Dispose();
            Tls.Dispose();
            _blocker?.Dispose();
            if (_api is not null)
            {
                await _api.StopAsync().ConfigureAwait(false);
                await _api.DisposeAsync().ConfigureAwait(false);
            }

            await Subscriptions.DisposeAsync().ConfigureAwait(false);
            await Services.DisposeAsync().ConfigureAwait(false);
            await _fronts.DisposeAsync().ConfigureAwait(false);
            Bench.Dispose();
            Directory.Delete(_folder, recursive: true);
        }

        private static int FreeFront()
        {
            while (true)
            {
                var port = Random.Shared.Next(ConfigServices.FrontBase + 1, ConfigServices.FrontBase + ConfigServices.FrontSpan);
                try
                {
                    using var socket = new TcpListener(IPAddress.Loopback, port);
                    socket.Start();
                    return port;
                }
                catch (SocketException)
                {
                }
            }
        }
    }

    private sealed class ScopeOverrides(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public SubscriptionServer? Subscriptions { get; set; }
        public IServiceScope CreateScope() => new OverriddenScope(inner.CreateScope(), this);

        private sealed class OverriddenScope(IServiceScope inner, ScopeOverrides owner) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => serviceType == typeof(SubscriptionServer) ? owner.Subscriptions : inner.ServiceProvider.GetService(serviceType);
            public void Dispose() => inner.Dispose();
        }
    }

    private sealed class EchoFronts(string folder) : IProxyRunner, IAsyncDisposable
    {
        private readonly Dictionary<string, WebApplication> _apps = new(StringComparer.Ordinal);
        public bool Refuse { get; set; }
        public Func<Task>? Starting { get; set; }

        public Task<ProxyState> EnableAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Down);
        public Task<ProxyState> StateAsync(string name, CancellationToken ct) => Task.FromResult(_apps.ContainsKey(name) ? ProxyState.Up : ProxyState.Down);

        public async Task<ProxyState> StartAsync(string name, CancellationToken ct)
        {
            await StopAsync(name, ct).ConfigureAwait(false);
            if (Starting is { } starting)
            {
                await starting().ConfigureAwait(false);
            }

            if (Refuse)
            {
                throw new InvalidOperationException("the test front refused to start");
            }
            var line = await File.ReadAllTextAsync(Path.Combine(folder, ProxyFile.Arguments(name)), ct).ConfigureAwait(false);
            var endpoint = new Uri(ChildProxies.Arguments(line)[0]);
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IHostLifetime, SharedPortLifetime>();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, endpoint.Port));
            var app = builder.Build();
            app.UseWebSockets();
            app.Run(async context =>
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                var buffer = new byte[128];
                var read = await socket.ReceiveAsync(buffer.AsMemory(), context.RequestAborted).ConfigureAwait(false);
                var body = Encoding.UTF8.GetBytes(name + ":" + Encoding.UTF8.GetString(buffer, 0, read.Count));
                await socket.SendAsync(body.AsMemory(), WebSocketMessageType.Text, true, context.RequestAborted).ConfigureAwait(false);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", context.RequestAborted).ConfigureAwait(false);
            });
            await app.StartAsync(ct).ConfigureAwait(false);
            _apps[name] = app;
            return ProxyState.Up;
        }

        public async Task<ProxyState> StopAsync(string name, CancellationToken ct)
        {
            if (_apps.Remove(name, out var app))
            {
                await app.StopAsync(ct).ConfigureAwait(false);
                await app.DisposeAsync().ConfigureAwait(false);
            }

            return ProxyState.Down;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var name in _apps.Keys.ToArray())
            {
                await StopAsync(name, default).ConfigureAwait(false);
            }
        }
    }

    private sealed class SharedPortLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class SharedPortLogger(Action refused) : ILogger<ServiceServer>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (level >= LogLevel.Warning && formatter(state, exception).Contains("stayed off TCP port", StringComparison.Ordinal))
            {
                refused();
            }
        }
    }
}
#pragma warning restore xUnit1030
