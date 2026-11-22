using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Routing.Udp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyThroughputTests
{
    [Theory]
    [InlineData("GET", 0, true)]
    [InlineData("GET", 1, false)]
    [InlineData("POST", 4096, true)]
    [InlineData("POST", 32768, true)]
    [InlineData("POST", 32769, false)]
    public void HttpEnvelopesKeepDistinctBodyLimits(string method, long length, bool accepted)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.ContentLength = length;
        Assert.Equal(accepted, XHttpRelay.Accepts(context, method));
    }

    [Fact]
    public async Task OversizedChunkedPostIsRejectedBeforeConnectingToRuntime()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Headers.TransferEncoding = "chunked";
        context.Request.Body = new MemoryStream(new byte[32769]);
        await XHttpRelay.PassAsync(context, 1, NullLogger.Instance, method: "POST").ConfigureAwait(false);
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuicFlowKeepsItsDestinationAfterReturnedEndpointsChange(bool ipv6)
    {
        using var backend = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var peer = new UdpClient(new IPEndPoint(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0));
        await using var front = new UdpFront(0, NullLogger.Instance);
        front.Configure([new("quic", ((IPEndPoint)backend.Client.LocalEndPoint!).Port)]);
        var endpoint = (IPEndPoint)peer.Client.LocalEndPoint!;
        var relay = front.Relay("quic", endpoint);
        var returned = front.Client(relay)!;
        returned.Port = 1;
        relay.Port = 1;
        Assert.Equal(endpoint, front.Client(front.Relay("quic", endpoint)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var target = new IPEndPoint(endpoint.Address, front.Port);
        await peer.SendAsync(new byte[] { 0x40, 1, 2 }, target, deadline.Token).ConfigureAwait(false);
        var received = await backend.ReceiveAsync(deadline.Token).ConfigureAwait(false);
        await backend.SendAsync(received.Buffer, received.RemoteEndPoint, deadline.Token).ConfigureAwait(false);
        var response = await peer.ReceiveAsync(deadline.Token).ConfigureAwait(false);
        Assert.Equal(received.Buffer, response.Buffer);
        front.Configure([]);
        Assert.Equal(0, front.FlowCount);
    }
}
