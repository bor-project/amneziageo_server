using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Carrier;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public partial class ServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalWebSocketRejectsAnExternalTargetDespiteItsHeader(bool anonymous)
    {
        using var bench = new Bench();
        using var identity = new ServiceTlsFixture();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with
        {
            Host = "local.example", ListenPort = 45120,
        }, default).ConfigureAwait(false)).Record!;
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, identity.Secure(new TransportProxyEntity
        {
            Name = "local", Host = "proxy.example.net", Port = 443, Path = "local",
            TargetsJson = ProxyTargetRules.Write(ProxyTargetRules.LocalOnly),
            AllowAnonymous = anonymous,
        }), default).ConfigureAwait(false);
        var template = (await bench.Templates.AddAsync(new ClientTemplate { Name = "selected", ProxyIds = [proxy.Id] }, default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone") with { TemplateId = template.Id }, default).ConfigureAwait(false)).Record!;
        var credential = TransportProxyStore.Credential(proxy, client.Id);
        var context = Upgrading("/local/tunnel");
        context.Response.Body = new MemoryStream();
        if (!anonymous)
        {
            context.Request.Headers.Authorization = "Bearer " + credential;
        }
        context.Request.Headers["X-AmneziaGeo-Target"] = "local.example:45120";
        var handshake = WsCarrier.Handshake(new WsEndpoint("proxy.example", 443, "local", ""), 45120, "key", "Bearer " + credential, "external.example");
        context.Request.Headers.SecWebSocketProtocol = handshake.Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Protocol:", StringComparison.Ordinal))[23..].Trim();
        var point = new ServicePoint(443, [new ServiceEndpoint(0, "proxy-" + proxy.Id, true, 9, 0, "local") { ProxyId = proxy.Id }], "", "");
        await Desk(bench, new SpeedTickets(bench.Clock)).AnswerAsync(context, point).ConfigureAwait(false);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Contains("proxy-target-denied", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    [Theory]
    [InlineData("other.example", 51820)]
    [InlineData("allowed.example", 51821)]
    public async Task RestrictedWebSocketRejectsTheActualTokenBeforeCallingItsBackend(string host, int port)
    {
        using var bench = new Bench();
        using var identity = new ServiceTlsFixture();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default).ConfigureAwait(false)).Record!;
        var store = new TransportProxyStore(bench.Db);
        var proxy = await store.SaveAsync(0, identity.Secure(new TransportProxyEntity
        {
            Name = "scoped", Host = "proxy.example.net", Port = 443, Path = "scoped",
            TargetsJson = ProxyTargetRules.Write(new ProxyTargets(true, [new("allowed.example", 51820)])),
        }), default).ConfigureAwait(false);
        var template = (await bench.Templates.AddAsync(new ClientTemplate { Name = "selected", ProxyIds = [proxy.Id] }, default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone") with { TemplateId = template.Id }, default).ConfigureAwait(false)).Record!;
        var credential = TransportProxyStore.Credential(proxy, client.Id);
        var context = Upgrading("/scoped/tunnel");
        context.Response.Body = new MemoryStream();
        context.Request.Headers.Authorization = "Bearer " + credential;
        context.Request.Headers["X-AmneziaGeo-Target"] = "allowed.example:51820";
        var handshake = WsCarrier.Handshake(new WsEndpoint("proxy.example", 443, "scoped", ""), port, "key", "Bearer " + credential, host);
        context.Request.Headers.SecWebSocketProtocol = handshake.Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Protocol:", StringComparison.Ordinal))[23..].Trim();
        var point = new ServicePoint(443, [new ServiceEndpoint(0, "proxy-" + proxy.Id, true, 9, 0, "scoped") { ProxyId = proxy.Id }], "", "");

        await Desk(bench, new SpeedTickets(bench.Clock)).AnswerAsync(context, point).ConfigureAwait(false);

        Assert.Equal(403, context.Response.StatusCode);
        using var error = JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal("proxy-target-denied", error.RootElement.GetProperty("error").GetString());
    }
}
