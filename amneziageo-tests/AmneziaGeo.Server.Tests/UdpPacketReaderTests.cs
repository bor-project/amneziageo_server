using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Udp;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class UdpPacketReaderTests
{
    [Fact]
    public async Task AGuardHoldBlocksOnlyItsBackendAndSource()
    {
        using var backend = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await using var front = new UdpFront(0, NullLogger.Instance);
        var port = ((IPEndPoint)backend.Client.LocalEndPoint!).Port;
        front.Configure([new("vpn", port, new UdpPacketReader(Config()))]);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        front.Block([(port, (IPEndPoint)client.Client.LocalEndPoint!)]);
        await client.SendAsync(Convert.FromHexString(Initiation), new IPEndPoint(IPAddress.Loopback, front.Port)).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);
        Assert.Equal(0, front.FlowCount);
        Assert.Equal(1, front.Snapshot().Dropped);
        front.Block([]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.SendAsync(Convert.FromHexString(Initiation), new IPEndPoint(IPAddress.Loopback, front.Port)).ConfigureAwait(false);
        Assert.Equal(Convert.FromHexString(Initiation), (await backend.ReceiveAsync(timeout.Token).ConfigureAwait(false)).Buffer);
    }

    [Fact]
    public async Task AnOversizedQuicTokenDoesNotStopTheSharedListener()
    {
        await using var front = new UdpFront(0, NullLogger.Instance);
        front.Configure([new("quic", 51820)]);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var packet = new byte[100];
        Convert.FromHexString("c0000000010000c0000000ffffff9c").CopyTo(packet, 0);
        await client.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, front.Port)).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);
        Assert.True(front.IsRunning);
        Assert.Equal(0, front.FlowCount);
    }

    private const string Initiation = "01000000785634124b525960676e757c838a91989fa6adb4bbc2c9d0d7dee5ecf3fa01080f161d242b323940474e555c636a71787f868d949ba2a9b0b7bec5ccd3dae1e8eff6fd040b121920272e353c434a51585f666d747b828990979ea5acb3bac1c8cfd6dde4ebf2f900070e151c232a313874ca057e0bad00eb7932e964711ced7600000000000000000000000000000000";
    private const string Protected = "000102030405060708090a0b29292929292929292929292908e5348ba19a3f0de0b5abe6909fe0f8571f3d9341bd607365e2ec1f64f20097785d769d95b80a8f725c673bfb7bab60c347490bbf7746ddd692a9dc0686364903e028d74edeb3d2d118a2cc9a86a269a2fb05212ac843094d894202294751a67a7fc4c0e39b1d7a984629387154e09558a780e9f2f5bd7b93d37aa270c83c9bef7f03a1920edca8ad667bf3376439494ca3b23451515151515151";

    [Fact]
    public void IndependentWireGuardMacVectorSelectsOnlyItsKey()
    {
        var packet = Convert.FromHexString(Initiation);
        var reader = new UdpPacketReader(Config());
        Assert.True(reader.Handshake(packet));
        Assert.Equal(0x12345678U, reader.Header(packet)!.Value.Sender);
        Assert.False(new UdpPacketReader(Config() with { PrivateKey = Convert.ToBase64String(Enumerable.Repeat((byte)5, 32).ToArray()) }).Handshake(packet));
        packet[35] ^= 1;
        Assert.False(reader.Handshake(packet));
    }

    [Fact]
    public void IndependentProtectedAwgVectorIncludesPaddingHeaderRangeAndTrailer()
    {
        var packet = Convert.FromHexString(Protected);
        var config = Config() with { Obfuscation = new()
        {
            S1 = 24, H1 = "11223340-11223350", HeaderProtectionKey = Convert.ToBase64String(Enumerable.Range(32, 32).Select(one => (byte)one).ToArray()),
            RandomTrailers = true,
        } };
        var reader = new UdpPacketReader(config);
        Assert.True(reader.Handshake(packet));
        Assert.Equal(0x12345678U, reader.Header(packet)!.Value.Sender);
        Assert.False(new UdpPacketReader(config with { Obfuscation = config.Obfuscation with { RandomTrailers = false } }).Handshake(packet));
        packet[50] ^= 1;
        Assert.False(reader.Handshake(packet));
        Assert.True(reader.Handshake(Convert.FromHexString(Initiation)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(11)]
    [InlineData(32)]
    [InlineData(91)]
    [InlineData(147)]
    [InlineData(148)]
    [InlineData(149)]
    [InlineData(1024)]
    public void UnknownPacketsDoNotBecomeHandshakes(int size)
    {
        Assert.False(new UdpPacketReader(Config()).Handshake(new byte[size]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASharedListenerReturnsRepliesAndRoutesRoamingByReceiverIndex(bool ipv6)
    {
        using var backend = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var reader = new UdpPacketReader(Config());
        await using var front = new UdpFront(0, NullLogger.Instance);
        front.Configure([new("vpn", ((IPEndPoint)backend.Client.LocalEndPoint!).Port, reader)]);
        var address = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        using var client = new UdpClient(new IPEndPoint(address, 0));
        var target = new IPEndPoint(address, front.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.SendAsync(Convert.FromHexString(Initiation), target, timeout.Token).ConfigureAwait(false);
        var received = await backend.ReceiveAsync(timeout.Token).ConfigureAwait(false);
        Assert.Equal(Convert.FromHexString(Initiation), received.Buffer);
        var response = new byte[92];
        BinaryPrimitives.WriteUInt32LittleEndian(response, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(4), 0x19482756);
        await backend.SendAsync(response, received.RemoteEndPoint, timeout.Token).ConfigureAwait(false);
        Assert.Equal(response, (await client.ReceiveAsync(timeout.Token).ConfigureAwait(false)).Buffer);
        using var moved = new UdpClient(new IPEndPoint(address, 0));
        var transport = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(transport, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(transport.AsSpan(4), 0x19482756);
        await moved.SendAsync(transport, target, timeout.Token).ConfigureAwait(false);
        var after = await backend.ReceiveAsync(timeout.Token).ConfigureAwait(false);
        Assert.NotEqual(received.RemoteEndPoint, after.RemoteEndPoint);
        await backend.SendAsync(transport, after.RemoteEndPoint, timeout.Token).ConfigureAwait(false);
        Assert.Equal(transport, (await moved.ReceiveAsync(timeout.Token).ConfigureAwait(false)).Buffer);
        Assert.Equal(2, front.FlowCount);
    }

    [Fact]
    public async Task CorruptHandshakesDoNotAllocateRelays()
    {
        await using var front = new UdpFront(0, NullLogger.Instance);
        front.Configure([new("vpn", 51820, new UdpPacketReader(Config()))]);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var packet = Convert.FromHexString(Initiation);
        packet[30] ^= 1;
        await client.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, front.Port)).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);
        Assert.Equal(0, front.FlowCount);
    }

    private static ServerConfig Config() => new()
    {
        Name = "awg1", ListenPort = 443,
        PrivateKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(one => (byte)one).ToArray()),
    };
}
