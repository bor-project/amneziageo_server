using System.Net;
using ServicePoint = AmneziaGeo.Server.Api.Services.ServicePoint;
using System.Net.Sockets;
using System.Text;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class LegacyHttpWsTests
{
    [Fact]
    public async Task ALegacyTlsWebSocketReachesItsBackendBesideAPlainHttpProxy()
    {
        using var bench = new Bench();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var accepted = false;
        var backend = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            var request = new byte[4096];
            var size = await stream.ReadAsync(request, deadline.Token).ConfigureAwait(false);
            Assert.Contains("GET /relay/session/events", Encoding.ASCII.GetString(request, 0, size));
            accepted = true;
            await stream.WriteAsync("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), deadline.Token).ConfigureAwait(false);
        }, deadline.Token);
        var desk = new ServiceDesk(bench.Scopes, new SpeedTickets(), [], new SubscriptionState(),
            new Life(), Options.Create(new JsonOptions()), NullLogger<ServiceDesk>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Scheme = "https";
        ServiceTlsFixture.SecureConnection(context);
        context.Request.Protocol = "HTTP/1.1";
        context.Request.Path = "/relay/session/events";
        context.Request.Headers.Connection = "Upgrade";
        context.Request.Headers.Upgrade = "websocket";
        context.Features.Set<IHttpUpgradeFeature>(new Upgrade());
        var legacy = new ServiceEndpoint(1, "awg1", true, port, 51820, "relay");
        var http = new ServiceEndpoint(-7, "http", false, port + 1, 0, "relay")
        {
            ProxyId = 7, XHttp = true, OriginHttp = true, UseCertificate = false,
        };
        await desk.AnswerAsync(context, new ServicePoint(443, [legacy, http], "", "")).ConfigureAwait(false);
        await backend.ConfigureAwait(false);
        Assert.True(accepted);
    }

    private sealed class Upgrade : IHttpUpgradeFeature
    {
        public bool IsUpgradableRequest => true;
        public Task<Stream> UpgradeAsync() => throw new InvalidOperationException("The backend rejects the test upgrade.");
    }

    private sealed class Life : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication()
        {
        }
    }
}
