using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.ClientUpdates;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Proxy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyEndpointTests
{
    [Fact]
    public async Task DestinationModesRoundTripThroughTheManagementApi()
    {
        using var bench = new Bench();
        var folder = Path.Combine(Path.GetTempPath(), "proxy-scope-api-" + Guid.NewGuid().ToString("N"));
        var panel = PanelDefaults.Settings with { Port = Free() };
        await bench.Panel.SeedAsync(panel, default).ConfigureAwait(false);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["Proxy:Directory"] = folder;
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, panel.Port));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddServerDatabase(bench.DatabasePath, new AuthOptions());
        builder.Services.AddSingleton(panel);
        builder.Services.AddSingleton(new WebOptions());
        builder.Services.AddSingleton(new DnsState());
        builder.Services.AddSingleton<IHostNetwork>(new Ledger());
        builder.Services.AddSingleton<IHostCommands>(new Tools());
        builder.Services.AddFirewall();
        builder.Services.AddEndpointServices(builder.Configuration);
        builder.Services.AddSingleton<IProxyRunner>(new Fronts());
        builder.Services.AddSubscriptions();
        builder.Services.AddClientUpdates();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items["amneziageo.caller"] = new Principal(1, "tester", AuthScheme.Password, 1, Scopes.All.ToHashSet(), "admin");
            await next(context).ConfigureAwait(false);
        });
        app.MapTransportProxies();
        try
        {
            await app.StartAsync().ConfigureAwait(false);
            using var api = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{panel.Port}/api/proxies/") };
            var fresh = await api.GetFromJsonAsync<ProxyResponse>("draft").ConfigureAwait(false);
            Assert.True(fresh!.Targets!.Local);
            var request = new ProxyRequest("relay", "websocket", "proxy.example", 443, "relay", true, Hosted: false);
            using var created = await api.PostAsJsonAsync("", request).ConfigureAwait(false);
            created.EnsureSuccessStatusCode();
            var proxy = (await created.Content.ReadFromJsonAsync<ProxyResponse>().ConfigureAwait(false))!;
            Assert.True(proxy.Targets!.Local);
            var id = proxy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

            using var selected = await api.PutAsJsonAsync(id, request with
            {
                Targets = new ProxyTargets(true, [new("REMOTE.EXAMPLE.", 45120)]),
            }).ConfigureAwait(false);
            selected.EnsureSuccessStatusCode();
            var restricted = (await selected.Content.ReadFromJsonAsync<ProxyResponse>().ConfigureAwait(false))!.Targets!;
            Assert.False(restricted.Local);
            Assert.Equal(new ProxyDestination("remote.example", 45120), Assert.Single(restricted.Destinations));

            using var invalid = await api.PutAsJsonAsync(id, request with
            {
                Targets = new ProxyTargets(true, [new("remote.example", 45120)], true),
            }).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains("bad-proxy-targets", await invalid.Content.ReadAsStringAsync().ConfigureAwait(false));
            using var renamed = await api.PutAsJsonAsync(id, request with { Name = "renamed" }).ConfigureAwait(false);
            renamed.EnsureSuccessStatusCode();
            var preserved = (await renamed.Content.ReadFromJsonAsync<ProxyResponse>().ConfigureAwait(false))!.Targets!;
            Assert.Equal(restricted.Restricted, preserved.Restricted);
            Assert.Equal(restricted.Local, preserved.Local);
            Assert.Equal(restricted.Destinations, preserved.Destinations);

            using var any = await api.PutAsJsonAsync(id, request with { Targets = new ProxyTargets(false, []) }).ConfigureAwait(false);
            any.EnsureSuccessStatusCode();
            Assert.False((await any.Content.ReadFromJsonAsync<ProxyResponse>().ConfigureAwait(false))!.Targets!.Restricted);
            using var local = await api.PutAsJsonAsync(id, request with { Targets = ProxyTargetRules.LocalOnly }).ConfigureAwait(false);
            local.EnsureSuccessStatusCode();
            var listed = Assert.Single((await api.GetFromJsonAsync<ProxyResponse[]>("").ConfigureAwait(false))!);
            Assert.True(listed.Targets!.Local);
            Assert.Empty(listed.Targets.Destinations);

            using var mintedResponse = await api.PostAsJsonAsync(id + "/tokens", new ProxyTokenRequest("phone")).ConfigureAwait(false);
            mintedResponse.EnsureSuccessStatusCode();
            Assert.True(mintedResponse.Headers.CacheControl!.NoStore);
            var minted = (await mintedResponse.Content.ReadFromJsonAsync<MintedProxyToken>().ConfigureAwait(false))!;
            var tokenText = await api.GetStringAsync(id + "/tokens").ConfigureAwait(false);
            Assert.DoesNotContain(minted.Secret, tokenText);
            var tokens = System.Text.Json.JsonSerializer.Deserialize<ProxyTokenInfo[]>(tokenText, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            Assert.Equal(minted.Token, Assert.Single(tokens));
            Assert.DoesNotContain(minted.Secret, await api.GetStringAsync("").ConfigureAwait(false));

            using var anonymous = await api.PutAsJsonAsync(id, request with { AllowAnonymous = true }).ConfigureAwait(false);
            anonymous.EnsureSuccessStatusCode();
            Assert.True((await anonymous.Content.ReadFromJsonAsync<ProxyResponse>().ConfigureAwait(false))!.AllowAnonymous);
            using var oldRequest = await api.PutAsJsonAsync(id, request).ConfigureAwait(false);
            oldRequest.EnsureSuccessStatusCode();
            Assert.True((await oldRequest.Content.ReadFromJsonAsync<ProxyResponse>().ConfigureAwait(false))!.AllowAnonymous);
            Assert.Single((await api.GetFromJsonAsync<ProxyTokenInfo[]>(id + "/tokens").ConfigureAwait(false))!);
            using var exported = await api.GetAsync(id + "/export").ConfigureAwait(false);
            exported.EnsureSuccessStatusCode();
            var catalog = (await exported.Content.ReadFromJsonAsync<ProxyCatalog>().ConfigureAwait(false))!;
            var entry = Assert.Single(catalog.Proxies);
            Assert.True(entry.AllowAnonymous);
            Assert.Contains(ProxyAccess.HttpIdentity(minted.Secret), entry.BearerIds!);
            using var revoked = await api.DeleteAsync(id + "/tokens/" + minted.Token.Id).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
            Assert.Empty((await api.GetFromJsonAsync<ProxyTokenInfo[]>(id + "/tokens").ConfigureAwait(false))!);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Theory]
    [InlineData("subscription.example.net", false)]
    [InlineData("proxy.example.net", true)]
    [InlineData("127.0.0.1", false)]
    public async Task SharedServicePortSelectsCertificateWithAndWithoutSni(string targetHost, bool usesProxyCertificate)
    {
        using var bench = new Bench();
        var folder = Path.Combine(Path.GetTempPath(), "amneziageo-proxy-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var panel = PanelDefaults.Settings with { Port = Free() };
        await bench.Panel.SeedAsync(panel, default).ConfigureAwait(false);
        var port = Free();
        var proxyCertificate = Certificate(folder, "proxy");
        var subscriptionCertificate = Certificate(folder, "subscription");
        await new TransportProxyStore(bench.Db).SaveAsync(0, new TransportProxyEntity
        {
            Name = "relay", Host = "proxy.example.net", Port = port, Path = "relay",
            Certificate = proxyCertificate.Chain, CertificateKey = proxyCertificate.Key, Pin = proxyCertificate.Pin,
        }, default).ConfigureAwait(false);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["Proxy:Directory"] = folder;
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, panel.Port));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddServerDatabase(bench.DatabasePath, new AuthOptions());
        builder.Services.AddSingleton(panel);
        builder.Services.AddSingleton(new WebOptions());
        builder.Services.AddSingleton(new ProxyCertificates());
        builder.Services.AddSingleton(new DnsState());
        builder.Services.AddSingleton<IHostNetwork>(new Ledger());
        builder.Services.AddSingleton<IHostCommands>(new Tools());
        builder.Services.AddFirewall();
        builder.Services.AddEndpointServices(builder.Configuration);
        var fronts = new Fronts();
        builder.Services.AddSingleton<IProxyRunner>(fronts);
        builder.Services.AddSubscriptions();
        builder.Services.AddClientUpdates();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items["amneziageo.caller"] = new Principal(1, "tester", AuthScheme.Password, 1, Scopes.All.ToHashSet(), "admin");
            await next(context).ConfigureAwait(false);
        });
        app.MapSubscriptions();
        app.MapTransportProxies();
        try
        {
            await app.StartAsync().ConfigureAwait(false);
            using var api = new HttpClient();
            foreach (var unsafeMode in new[]
            {
                new ProxyRequest("plain", "websocket", "proxy.example.net", port, "plain", true, UseCertificate: false),
                new ProxyRequest("origin", "http", "proxy.example.net", port, "origin", true, OriginHttp: true, UseCertificate: true, ProtocolVersion: 2),
            })
            {
                using var refused = await api.PostAsJsonAsync($"http://127.0.0.1:{panel.Port}/api/proxies", unsafeMode).ConfigureAwait(false);
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                Assert.Contains("proxy-tls-required", await refused.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            using var saved = await api.PutAsJsonAsync($"http://127.0.0.1:{panel.Port}/api/subscription", new SubscriptionRequest(
                true, true, [], ["subscription.example.net"], port, false, "sub",
                subscriptionCertificate.Chain, subscriptionCertificate.Key, 12, "test")).ConfigureAwait(false);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync().ConfigureAwait(false));
            var observed = string.Empty;
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(IPAddress.Loopback, port, ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                },
                SslOptions = new()
                {
                    RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    {
                        observed = certificate!.GetCertHashString();
                        return observed == proxyCertificate.Thumbprint || observed == subscriptionCertificate.Thumbprint;
                    },
                },
            };
            using var subscriber = new HttpClient(handler);
            using var response = await subscriber.GetAsync($"https://{targetHost}:{port}/sub/missing").ConfigureAwait(false);
            Assert.Equal(usesProxyCertificate ? proxyCertificate.Thumbprint : subscriptionCertificate.Thumbprint, observed);
            var serviceIdentity = ServiceTrust.Of(AmneziaGeo.Server.Awg.Config.ConfigDefaults.Fresh("services") with
                { Host = targetHost, ListenPort = port }, new WebOptions(), panel,
                subscription: app.Services.GetRequiredService<SubscriptionState>().Current,
                proxies: app.Services.GetRequiredService<ProxyCertificates>(),
                configured: await new TransportProxyStore(bench.Db).ListAsync(default).ConfigureAwait(false),
                selectedFiles: await app.Services.GetRequiredService<ServiceServer>().CertificateFilesAsync(port, default).ConfigureAwait(false));
            Assert.Equal((usesProxyCertificate ? proxyCertificate.Pin : subscriptionCertificate.Pin).ToLowerInvariant(), serviceIdentity!.Pin);

            fronts.Down = true;
            var registered = Assert.Single((await api.GetFromJsonAsync<ProxyResponse[]>($"http://127.0.0.1:{panel.Port}/api/proxies").ConfigureAwait(false))!);
            using var changed = await api.PutAsJsonAsync($"http://127.0.0.1:{panel.Port}/api/proxies/{registered.Id}", new ProxyRequest(
                registered.Name, "websocket", registered.Host, port, registered.Path, true,
                proxyCertificate.Chain, proxyCertificate.Key, Pin: proxyCertificate.Pin)).ConfigureAwait(false);
            changed.EnsureSuccessStatusCode();
            var failed = await changed.Content.ReadFromJsonAsync<ProxyResponse>().ConfigureAwait(false);
            Assert.NotEmpty(failed!.Fault);
            var listed = Assert.Single((await api.GetFromJsonAsync<ProxyResponse[]>($"http://127.0.0.1:{panel.Port}/api/proxies").ConfigureAwait(false))!);
            Assert.Equal(failed.Fault, listed.Fault);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
            Directory.Delete(folder, recursive: true);
        }
    }

    private static (string Chain, string Key, string Thumbprint, string Pin) Certificate(string folder, string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name + ".example.net", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(name + ".example.net");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        var chain = Path.Combine(folder, name + ".pem");
        var privateKey = Path.Combine(folder, name + ".key");
        File.WriteAllText(chain, certificate.ExportCertificatePem());
        File.WriteAllText(privateKey, key.ExportPkcs8PrivateKeyPem());
        return (chain, privateKey, certificate.Thumbprint, Convert.ToHexString(SHA256.HashData(certificate.RawData)));
    }

    private static int Free()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class Fronts : IProxyRunner
    {
        public bool Down { get; set; }

        public Task<ProxyState> EnableAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Up);
        public Task<ProxyState> StartAsync(string name, CancellationToken ct) => Task.FromResult(Down ? new ProxyState(false, "front stopped") : ProxyState.Up);
        public Task<ProxyState> StopAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Down);
        public Task<ProxyState> StateAsync(string name, CancellationToken ct) => Task.FromResult(Down ? ProxyState.Down : ProxyState.Up);
    }
}
