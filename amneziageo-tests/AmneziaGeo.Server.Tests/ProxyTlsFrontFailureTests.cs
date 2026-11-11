using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyTlsFrontFailureTests
{
    [Fact]
    public async Task TrustedForwardedHttpsWithoutPhysicalTlsRejectsAValidProxyCredential()
    {
        using var bench = new Bench();
        using var identity = new ProxyTlsFixture();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var targetPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, identity.Proxy(), default).ConfigureAwait(false);
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("vpn"), default).ConfigureAwait(false)).Record!;
        var template = (await bench.Templates.AddAsync(new ClientTemplate { Name = "selected", ProxyIds = [proxy.Id] }, default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "peer") with { TemplateId = template.Id }, default).ConfigureAwait(false)).Record!;
        var credential = TransportProxyStore.Credential(proxy, client.Id);
        Assert.Contains(credential, await store.CredentialsAsync(proxy, default).ConfigureAwait(false));
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(new PanelPlace(0, "", false));
        var app = builder.Build();
        await using var disposing = app.ConfigureAwait(false);
        var desk = new ServiceDesk(bench.Scopes, new SpeedTickets(), [], new SubscriptionState(), app.Lifetime,
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Http.Json.JsonOptions()),
            NullLogger<ServiceDesk>.Instance);
        var point = new AmneziaGeo.Server.Api.Services.ServicePoint(8443,
            [new ServiceEndpoint(-proxy.Id, "proxy", true, 1, targetPort, proxy.Path) { ProxyId = proxy.Id, UseCertificate = true }], "", "");
        var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto };
        forwarded.KnownProxies.Add(IPAddress.Loopback);
        app.UseForwardedHeaders(forwarded);
        var seen = false;
        app.Run(async context =>
        {
            Assert.Equal("https", context.Request.Scheme);
            Assert.True(context.Request.Path.StartsWithSegments("/" + proxy.Path));
            Assert.True(context.Features.Get<IHttpUpgradeFeature>()?.IsUpgradableRequest == true);
            Assert.Null(context.Features.Get<ITlsConnectionFeature>());
            Assert.False(app.Services.GetRequiredService<PanelPlace>().Secure);
            seen = true;
            await desk.AnswerAsync(context, point).ConfigureAwait(false);
        });
        await app.StartAsync().ConfigureAwait(false);
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
            using var request = new HttpRequestMessage(HttpMethod.Get, Assert.Single(app.Urls) + "/" + proxy.Path + "/events")
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
            request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
            request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + credential);
            request.Headers.TryAddWithoutValidation("X-AmneziaGeo-Target", "127.0.0.1:" + targetPort);
            using var response = await http.SendAsync(request).ConfigureAwait(false);
            Assert.True(seen);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
            Assert.DoesNotContain(response.Headers, header => header.Key.StartsWith("X-WG-", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, udp.Available);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task IncompletePublicTlsPostReturnsEmpty404WithinTheFrontDeadline()
    {
        using var identity = new ProxyTlsFixture("127.0.0.1");
        using var certificate = X509Certificate2.CreateFromPemFile(identity.Chain, identity.Key);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate));
        });
        var app = builder.Build();
        await using var disposing = app.ConfigureAwait(false);
        app.MapPost("/relay/", async (HttpContext context) =>
            await XHttpRelay.PassAsync(context, 1, NullLogger.Instance, method: "POST").ConfigureAwait(false));
        app.MapFallback(context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
        await app.StartAsync().ConfigureAwait(false);
        try
        {
            var port = new Uri(Assert.Single(app.Urls)).Port;
            var known = await ReadAsync("/relay/").ConfigureAwait(false);
            var unknown = await ReadAsync("/unknown/").ConfigureAwait(false);
            Assert.Equal(unknown.Headers, known.Headers);
            Assert.True(known.Elapsed >= TimeSpan.FromSeconds(4));
            Assert.True(known.Elapsed < TimeSpan.FromSeconds(9));
            Assert.True(unknown.Elapsed < known.Elapsed);

            async Task<(string[] Headers, TimeSpan Elapsed)> ReadAsync(string path)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var socket = new TcpClient();
                var started = Stopwatch.StartNew();
                await socket.ConnectAsync(IPAddress.Loopback, port, deadline.Token).ConfigureAwait(false);
                using var tls = new SslStream(socket.GetStream(), false, (_, peer, _, _) =>
                    peer is X509Certificate2 leaf && ProxySecurity.Check(leaf, new X509Certificate2Collection(),
                        "127.0.0.1", identity.Pin, DateTimeOffset.UtcNow) is null);
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "127.0.0.1",
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                }, deadline.Token).ConfigureAwait(false);
                var request = System.Text.Encoding.ASCII.GetBytes($"POST {path} HTTP/1.1\r\nHost: localhost\r\nContent-Length: 1\r\n\r\n");
                await tls.WriteAsync(request, deadline.Token).ConfigureAwait(false);
                await tls.FlushAsync(deadline.Token).ConfigureAwait(false);
                using var reader = new StreamReader(tls, System.Text.Encoding.ASCII, false, 1024, leaveOpen: true);
                var headers = new List<string>();
                while (await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false) is { Length: > 0 } line)
                {
                    if (!line.StartsWith("Date:", StringComparison.OrdinalIgnoreCase))
                    {
                        headers.Add(line.ToLowerInvariant());
                    }
                }
                Assert.Equal("http/1.1 404 not found", headers[0]);
                Assert.Contains("content-length: 0", headers);
                Assert.DoesNotContain(headers, line => line.StartsWith("x-wg-", StringComparison.Ordinal)
                    || line.StartsWith("content-type:", StringComparison.Ordinal) || line.StartsWith("x-accel-buffering:", StringComparison.Ordinal));
                return (headers.ToArray(), started.Elapsed);
            }
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }
}
