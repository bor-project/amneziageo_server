using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Routing.Relay;

namespace AmneziaGeo.Server.Tests;

public class RelayTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TheRelayCarriesADatagramToTheTargetAndTheAnswerBack()
    {
        using var stopping = new CancellationTokenSource(Patience);
        using var far = Bind();
        using var relay = new UdpRelay(Loose(), Where(far), TimeSpan.FromSeconds(30));
        var running = relay.RunAsync(stopping.Token);
        var echo = EchoAsync(far, stopping.Token);

        using var client = Bind();
        var door = new IPEndPoint(IPAddress.Loopback, relay.Port);
        await client.SendToAsync(new byte[] { 1, 2, 3 }, SocketFlags.None, door, stopping.Token);

        var buffer = new byte[64];
        var back = await client.ReceiveFromAsync(buffer, SocketFlags.None, Loose(), stopping.Token);

        Assert.Equal(3, back.ReceivedBytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer[..back.ReceivedBytes]);
        Assert.Equal(door, back.RemoteEndPoint);
        Assert.Equal(1, relay.Clients);

        await echo;
        await stopping.CancelAsync();
        await running;
    }

    [Fact]
    public async Task EveryClientOfTheRelayGetsASocketOfItsOwn()
    {
        using var stopping = new CancellationTokenSource(Patience);
        using var far = Bind();
        using var relay = new UdpRelay(Loose(), Where(far), TimeSpan.FromSeconds(30));
        var running = relay.RunAsync(stopping.Token);
        var door = new IPEndPoint(IPAddress.Loopback, relay.Port);

        using var one = Bind();
        using var two = Bind();
        await one.SendToAsync(new byte[] { 1 }, SocketFlags.None, door, stopping.Token);
        await TakeAsync(far, stopping.Token);
        await two.SendToAsync(new byte[] { 2 }, SocketFlags.None, door, stopping.Token);
        await TakeAsync(far, stopping.Token);

        Assert.Equal(2, relay.Clients);

        await stopping.CancelAsync();
        await running;
    }

    private static Socket Bind()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return socket;
    }

    private static IPEndPoint Where(Socket socket) => (IPEndPoint)socket.LocalEndPoint!;

    private static IPEndPoint Loose() => new(IPAddress.Loopback, 0);

    private static async Task<SocketReceiveFromResult> TakeAsync(Socket socket, CancellationToken ct)
    {
        var buffer = new byte[64];

        return await socket.ReceiveFromAsync(buffer, SocketFlags.None, Loose(), ct);
    }

    private static async Task EchoAsync(Socket socket, CancellationToken ct)
    {
        var buffer = new byte[64];
        var read = await socket.ReceiveFromAsync(buffer, SocketFlags.None, Loose(), ct);
        await socket.SendToAsync(buffer.AsMemory(0, read.ReceivedBytes), SocketFlags.None, read.RemoteEndPoint, ct);
    }
}
