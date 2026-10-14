using System.Buffers.Binary;
using AmneziaGeo.Server.Awg.Netlink;

namespace AmneziaGeo.Server.Tests;

public class NameQueueTests
{
    [Fact]
    public void TheRequestTakesTheQueueWholePacketsAndLetsThemGoWhenItIsFull()
    {
        var request = NetfilterQueue.Config(1, 7318, 0xFFFF, 4096);

        Assert.Equal(64, request.Length);
        Assert.Equal(64u, BinaryPrimitives.ReadUInt32LittleEndian(request));
        Assert.Equal(0x0302, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(4)));
        Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(6)));
        Assert.Equal(7318, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(18)));
        Assert.Equal((8, 1, 1), (Short(request, 20), Short(request, 22), request[24]));
        Assert.Equal((9, 2, 0xFFFFu, 2), (Short(request, 28), Short(request, 30), Big(request, 32), request[36]));
        Assert.Equal((8, 3, 4096u), (Short(request, 40), Short(request, 42), Big(request, 44)));
        Assert.Equal((8, 4, 1u), (Short(request, 48), Short(request, 50), Big(request, 52)));
        Assert.Equal((8, 5, 1u), (Short(request, 56), Short(request, 58), Big(request, 60)));
    }

    [Fact]
    public void AVerdictNamesThePacketAndCarriesTheMarkWhenThereIsOne()
    {
        var plain = NetfilterQueue.Verdict(7318, 0x01020304, NetfilterQueue.Accept, null);
        var marked = NetfilterQueue.Verdict(7318, 77, NetfilterQueue.Repeat, 0x0008_0000);

        Assert.Equal(32, plain.Length);
        Assert.Equal(0x0301, BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(4)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(6)));
        Assert.Equal(7318, BinaryPrimitives.ReadUInt16BigEndian(plain.AsSpan(18)));
        Assert.Equal((12, 2, 1u, 0x01020304u), (Short(plain, 20), Short(plain, 22), Big(plain, 24), Big(plain, 28)));
        Assert.Equal(40, marked.Length);
        Assert.Equal((4u, 77u), (Big(marked, 24), Big(marked, 28)));
        Assert.Equal((8, 3, 0x0008_0000u), (Short(marked, 32), Short(marked, 34), Big(marked, 36)));
    }

    [Fact]
    public void ThePacketsOfABatchAreReadWithTheirNumbersMarksAndBytes()
    {
        var payload = new byte[] { 0x45, 0, 0, 28, 1, 2, 3, 4 };
        var batch = Packet(9, 0xA601, 3, payload).Concat(Error()).Concat(Packet(10, 0, 0, [0x60])).ToArray();
        var packets = new List<QueuedPacket>();

        NetfilterQueue.Parse(batch, packets);

        Assert.Equal(2, packets.Count);
        Assert.Equal((9u, 0xA601u, 3u), (packets[0].Id, packets[0].Mark, packets[0].Inbound));
        Assert.Equal(payload, packets[0].Payload);
        Assert.Equal((10u, 0u, 0u), (packets[1].Id, packets[1].Mark, packets[1].Inbound));
        Assert.Equal(new byte[] { 0x60 }, packets[1].Payload);
    }

    [Fact]
    public void TheKnockIsARequestTheKernelAnswersWithAnAcknowledgmentAlone()
    {
        var knock = NetlinkSocket.Knock();

        Assert.Equal(16, knock.Length);
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32LittleEndian(knock));
        Assert.Equal(1, Short(knock, 4));
        Assert.Equal(5, Short(knock, 6));
    }

    private static int Short(byte[] message, int at) => BinaryPrimitives.ReadUInt16LittleEndian(message.AsSpan(at));

    private static uint Big(byte[] message, int at) => BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(at));

    // Builds the message the kernel hands a queued packet over in.
    private static byte[] Packet(uint id, uint mark, uint inbound, byte[] payload)
    {
        var body = new List<byte>();
        body.AddRange([2, 0, 0x1C, 0x96]);
        Attribute(body, 1, [.. Bytes(id), 0x08, 0x00, 0, 0]);
        if (mark != 0)
        {
            Attribute(body, 3, Bytes(mark));
        }

        if (inbound != 0)
        {
            Attribute(body, 5, Bytes(inbound));
        }

        Attribute(body, 10, payload);
        var message = new byte[16 + body.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)message.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4), 0x0300);
        body.CopyTo(message, 16);

        return message;
    }

    private static byte[] Error()
    {
        var message = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(message, 36);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4), 2);

        return message;
    }

    private static void Attribute(List<byte> body, ushort type, byte[] value)
    {
        var head = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(head, (ushort)(4 + value.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(2), type);
        body.AddRange(head);
        body.AddRange(value);
        while (body.Count % 4 != 0)
        {
            body.Add(0);
        }
    }

    private static byte[] Bytes(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);

        return bytes;
    }
}
