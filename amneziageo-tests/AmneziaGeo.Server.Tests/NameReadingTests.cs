using System.Buffers.Binary;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Routing.Names;

namespace AmneziaGeo.Server.Tests;

public class NameReadingTests
{
    private static readonly byte[] Connection = Convert.FromHexString("8394c8f03e515708");

    [Fact]
    public void TheFirstMessageOfTlsNamesTheHost()
    {
        var hello = Hello("video.example.org");

        Assert.Equal(new SeenName(NameReading.Found, "video.example.org"), TlsHello.Read(hello));
    }

    [Fact]
    public void AMessageCutShortWaitsForTheRest()
    {
        var hello = Hello("video.example.org");
        var name = hello.AsSpan().IndexOf("video.example.org"u8);

        Assert.Equal(SeenName.More, TlsHello.Read([]));
        Assert.Equal(SeenName.More, TlsHello.Read(hello.AsSpan(0, 3)));
        Assert.Equal(SeenName.More, TlsHello.Read(hello.AsSpan(0, name)));
        Assert.Equal(SeenName.More, TlsHello.Read(hello.AsSpan(0, name + 5)));
        Assert.Equal(NameReading.Found, TlsHello.Read(hello.AsSpan(0, name + 17)).Reading);
    }

    [Fact]
    public void AMessageSpreadOverTwoRecordsIsPutTogether()
    {
        var hello = Hello("video.example.org");
        var half = hello.AsSpan().IndexOf("video.example.org"u8) - 2;
        var spread = Record(hello.AsSpan(5, half)).Concat(Record(hello.AsSpan(5 + half))).ToArray();

        Assert.Equal(SeenName.More, TlsHello.Read(spread.AsSpan(0, half + 5)));
        Assert.Equal(new SeenName(NameReading.Found, "video.example.org"), TlsHello.Read(spread));
    }

    [Fact]
    public void BytesThatAreNotTheFirstMessageOfTlsCarryNoName()
    {
        Assert.Equal(SeenName.None, TlsHello.Read("GET / HTTP/1.1\r\n\r\n"u8));
        Assert.Equal(SeenName.None, TlsHello.Read([0x17, 3, 3, 0, 2, 1, 2]));
        Assert.Equal(SeenName.None, TlsHello.Read([0x16, 3, 1, 0, 4, 2, 0, 0, 0]));
        Assert.Equal(SeenName.None, TlsHello.Read(Hello("192.0.2.7")));
    }

    [Fact]
    public void ANameIsReadInLowerCaseAndOnlyWhenItIsAHostName()
    {
        Assert.Equal(new SeenName(NameReading.Found, "video.example.org"), SeenName.Of("Video.Example.ORG."u8));
        Assert.Equal(SeenName.None, SeenName.Of("bad name"u8));
        Assert.Equal(SeenName.None, SeenName.Of([]));
        Assert.Equal(SeenName.None, SeenName.Of("."u8));
    }

    [Theory]
    [InlineData("GET /a HTTP/1.1\r\nUser-Agent: x\r\nHost: Video.Example.org:8080\r\n\r\n", "video.example.org")]
    [InlineData("POST / HTTP/1.1\r\nhost:example.net\r\nAccept: */*\r\n\r\n", "example.net")]
    [InlineData("HEAD / HTTP/1.0\nHOST:   example.com  \n\n", "example.com")]
    public void TheHeadOfARequestNamesTheHost(string head, string name)
    {
        Assert.Equal(new SeenName(NameReading.Found, name), HttpHead.Read(Encoding.ASCII.GetBytes(head)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("GE")]
    [InlineData("GET / HTTP/1.1\r\nUser-Agent: x\r\nHos")]
    [InlineData("GET / HTTP/1.1\r\nHost: exam")]
    public void AHeadCutShortWaitsForTheRest(string head)
    {
        Assert.Equal(SeenName.More, HttpHead.Read(Encoding.ASCII.GetBytes(head)));
    }

    [Theory]
    [InlineData("GET / HTTP/1.0\r\nAccept: */*\r\n\r\n")]
    [InlineData("SSH-2.0-OpenSSH_9.6\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost: [2001:db8::1]\r\n\r\n")]
    public void ARequestWithoutAHostNameCarriesNone(string head)
    {
        Assert.Equal(SeenName.None, HttpHead.Read(Encoding.ASCII.GetBytes(head)));
    }

    [Fact]
    public void TheKeysOfAnInitialPacketAreTheOnesTheStandardGives()
    {
        var one = QuicInitial.Keys(QuicInitial.One, Connection);
        var two = QuicInitial.Keys(QuicInitial.Two, Connection);

        Assert.Equal("1f369613dd76d5467730efcbe3b1a22d", Convert.ToHexStringLower(one!.Key));
        Assert.Equal("fa044b2f42a3fd3b46fb255c", Convert.ToHexStringLower(one.Iv));
        Assert.Equal("9f50449e04a0e810283a1e9933adedd2", Convert.ToHexStringLower(one.Header));
        Assert.Equal("8b1a0bc121284290a29e0971b5cd045d", Convert.ToHexStringLower(two!.Key));
        Assert.Equal("91f73e2351d8fa91660e909f", Convert.ToHexStringLower(two.Iv));
        Assert.Equal("45b95e15235d6f45a6b19cbcb0294ba9", Convert.ToHexStringLower(two.Header));
        Assert.Null(QuicInitial.Keys(0x12345678, Connection));
    }

    [Theory]
    [InlineData(QuicInitial.One)]
    [InlineData(QuicInitial.Two)]
    public void AnInitialPacketOfQuicNamesTheHost(uint version)
    {
        var message = Hello("video.example.org")[5..];
        var datagram = Initial(version, Connection, 0, (0, message));

        var pieces = QuicInitial.Open(datagram);

        Assert.NotNull(pieces);
        Assert.Equal(new SeenName(NameReading.Found, "video.example.org"), new QuicHello().Take(pieces!));
    }

    [Fact]
    public void AFirstMessageSpreadOverTwoPacketsOutOfOrderIsPutTogether()
    {
        var message = Hello("video.example.org")[5..];
        var third = message.Length / 3;
        var first = Initial(QuicInitial.One, Connection, 0, (third, message[third..(2 * third)]));
        var second = Initial(QuicInitial.One, Connection, 1, (2 * third, message[(2 * third)..]), (0, message[..third]));
        var hello = new QuicHello();

        Assert.Equal(SeenName.More, hello.Take(QuicInitial.Open(first)!));
        Assert.Equal(new SeenName(NameReading.Found, "video.example.org"), hello.Take(QuicInitial.Open(second)!));
    }

    [Fact]
    public void ADatagramTheReaderCannotOpenGivesNothing()
    {
        var message = Hello("video.example.org")[5..];
        var datagram = Initial(QuicInitial.One, Connection, 0, (0, message));
        var spoiled = datagram.ToArray();
        spoiled[^20] ^= 1;
        var other = datagram.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(other.AsSpan(1), 0x12345678);
        var handshake = datagram.ToArray();
        handshake[0] |= 0x20;

        Assert.NotNull(QuicInitial.Open(datagram));
        Assert.Null(QuicInitial.Open(spoiled));
        Assert.Null(QuicInitial.Open(other));
        Assert.Null(QuicInitial.Open(handshake));
        Assert.Null(QuicInitial.Open(datagram.AsSpan(0, 600)));
        Assert.Null(QuicInitial.Open([0x40, 1, 2, 3, 4, 5, 6, 7]));
        Assert.Null(QuicInitial.Open([]));
    }

    [Fact]
    public void WhatAClientSaysFirstOverTcpIsPutTogetherFromItsSegments()
    {
        var hello = Hello("video.example.org");
        var half = hello.AsSpan().IndexOf("video.example.org"u8) + 4;
        var stream = new StreamHello();

        stream.Start(1000);

        Assert.Equal(SeenName.More, stream.Take(1000, hello.AsSpan(0, half)));
        Assert.Equal(new SeenName(NameReading.Found, "video.example.org"), stream.Take(1000 + (uint)half, hello.AsSpan(half)));
        Assert.Equal(hello.Length, stream.Held);
    }

    [Fact]
    public void SegmentsOutOfOrderAndSentAgainAreStillPutTogether()
    {
        var hello = Hello("video.example.org");
        var name = hello.AsSpan().IndexOf("video.example.org"u8);
        var head = name + 3;
        var tail = name + 10;
        var start = uint.MaxValue - 10;
        var stream = new StreamHello();

        stream.Start(start);

        Assert.Equal(SeenName.More, stream.Take(start + (uint)tail, hello.AsSpan(tail)));
        Assert.Equal(SeenName.More, stream.Take(start, hello.AsSpan(0, head)));
        Assert.Equal(SeenName.More, stream.Take(start, hello.AsSpan(0, head)));
        Assert.Equal(head, stream.Held);
        Assert.Equal(
            new SeenName(NameReading.Found, "video.example.org"),
            stream.Take(start + (uint)head - 5, hello.AsSpan(head - 5, tail - head + 5)));
        Assert.Equal(hello.Length, stream.Held);
    }

    [Fact]
    public void TheFirstSegmentWithBytesStartsAStreamNobodyToldTheStartOf()
    {
        var stream = new StreamHello();
        var other = new StreamHello();

        Assert.Equal(
            new SeenName(NameReading.Found, "example.net"),
            stream.Take(77, "GET / HTTP/1.1\r\nHost: example.net\r\n\r\n"u8));
        Assert.Equal(SeenName.None, other.Take(77, [0x17, 3, 3, 0, 2, 1, 2]));
        Assert.Equal(SeenName.More, new StreamHello().Take(77, []));
    }

    [Fact]
    public void AStreamThatNeverEndsItsFirstMessageCarriesNoName()
    {
        var stream = new StreamHello();
        var seen = SeenName.More;
        var record = new byte[] { 0x16, 3, 1, 0x40, 0x00, 1, 0, 0x40, 0x00 };

        seen = stream.Take(0, record);
        for (var at = record.Length; at < 20000 && seen.Reading == NameReading.More; at += 1000)
        {
            seen = stream.Take((uint)at, new byte[1000]);
        }

        Assert.Equal(SeenName.None, seen);
        Assert.True(stream.Held <= 16384);
    }

    [Fact]
    public void TheHeaderOfASegmentTellsItsNumberItsFlagsAndWhereItsBytesStart()
    {
        var packet = new byte[20 + 32 + 3];
        packet[20 + 4] = 0x01;
        packet[20 + 7] = 0x02;
        packet[20 + 12] = 0x80;
        packet[20 + 13] = 0x18;

        Assert.Equal(new StreamPiece(0x01000002, 0x18, 52), StreamPiece.Read(packet, 20));
        Assert.Null(StreamPiece.Read(packet.AsSpan(0, 39), 20));
        Assert.Null(StreamPiece.Read(packet.AsSpan(0, 45), 20));
        Assert.Null(StreamPiece.Read(packet, -1));
        Assert.Null(StreamPiece.Read(new byte[40], 20));
    }

    // Returns the first record a client of TLS sends when it goes to a host.
    private static byte[] Hello(string host)
    {
        using var wire = new Wire();
        using var tls = new SslStream(wire);
        try
        {
            tls.AuthenticateAsClient(new SslClientAuthenticationOptions { TargetHost = host });
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException)
        {
        }

        return wire.Written.ToArray();
    }

    private static byte[] Record(ReadOnlySpan<byte> body)
    {
        var record = new byte[5 + body.Length];
        record[0] = 0x16;
        record[1] = 3;
        record[2] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(3), (ushort)body.Length);
        body.CopyTo(record.AsSpan(5));

        return record;
    }

    // Builds the Initial packet a client would send: the pieces as CRYPTO frames, padded, sealed and protected.
    private static byte[] Initial(uint version, byte[] connection, uint number, params (int Offset, byte[] Data)[] pieces)
    {
        var frames = new List<byte>();
        foreach (var (offset, data) in pieces)
        {
            frames.Add(6);
            frames.AddRange(Number((ulong)offset));
            frames.AddRange(Number((ulong)data.Length));
            frames.AddRange(data);
        }

        while (frames.Count < 1162)
        {
            frames.Add(0);
        }

        var keys = QuicInitial.Keys(version, connection)!;
        var head = new List<byte> { (byte)(0xC3 | ((version == QuicInitial.Two ? 1 : 0) << 4)) };
        head.AddRange(Big(version));
        head.Add((byte)connection.Length);
        head.AddRange(connection);
        head.Add(0);
        head.Add(0);
        head.AddRange(Number((ulong)(4 + frames.Count + 16)));
        var at = head.Count;
        head.AddRange(Big(number));

        var nonce = keys.Iv.ToArray();
        for (var one = 0; one < 4; one++)
        {
            nonce[11 - one] ^= (byte)(number >> (8 * one));
        }

        var closed = new byte[frames.Count];
        var tag = new byte[16];
        using (var gcm = new AesGcm(keys.Key, 16))
        {
            gcm.Encrypt(nonce, frames.ToArray(), closed, tag, head.ToArray());
        }

        var packet = head.Concat(closed).Concat(tag).ToArray();
        using var aes = Aes.Create();
        aes.Key = keys.Header;
        var mask = aes.EncryptEcb(packet.AsSpan(at + 4, 16), PaddingMode.None);
        packet[0] ^= (byte)(mask[0] & 0x0F);
        for (var one = 0; one < 4; one++)
        {
            packet[at + one] ^= mask[1 + one];
        }

        return packet;
    }

    private static byte[] Big(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);

        return bytes;
    }

    // Writes a number the way QUIC writes its lengths.
    private static byte[] Number(ulong value) =>
        value < 64 ? [(byte)value] : [(byte)(0x40 | (value >> 8)), (byte)value];

    /// <summary>
    /// A stream that keeps what is written to it and has nothing to read.
    /// </summary>
    private sealed class Wire : Stream
    {
        public MemoryStream Written { get; } = new();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => Written.Length;

        public override long Position
        {
            get => Written.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);
    }
}
