using System.Buffers.Binary;
using System.Text;
using AmneziaGeo.Server.Awg.Netlink;

namespace AmneziaGeo.Server.Tests;

public class NetfilterCounterTests
{
    private const ushort NewObject = 0x0A12;

    [Fact]
    public void TheRequestNamesTheTableTheCounterAndItsKind()
    {
        var request = NetfilterCounter.Request(7, "filter", "seen");

        Assert.Equal((uint)request.Length, BinaryPrimitives.ReadUInt32LittleEndian(request));
        Assert.Equal((ushort)0x0A13, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(4)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(6)));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(8)));
        Assert.Equal([1, 0, 0, 0], request[16..20]);
        var named = NetlinkAttributes.Map(request.AsMemory(20));
        Assert.Equal("filter\0", Encoding.ASCII.GetString(named[1].Span));
        Assert.Equal("seen\0", Encoding.ASCII.GetString(named[2].Span));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(named[3].Span));
    }

    [Fact]
    public void TheAnswerGivesThePacketsOfTheCounter()
    {
        var answer = Answer(writer =>
        {
            writer.PutString(1, "filter");
            writer.PutString(2, "seen");
            writer.PutBytes(3, [0, 0, 0, 1]);
            var data = writer.BeginNested(4);
            writer.PutBytes(1, Big(123456));
            writer.PutBytes(2, Big(5_000_000_000));
            writer.EndNested(data);
        });

        Assert.Equal(5_000_000_000, NetfilterCounter.Packets([answer]));
    }

    [Fact]
    public void AnAnswerWithoutACounterGivesNothing()
    {
        var bare = Answer(writer => writer.PutString(1, "filter"));
        var cut = Answer(writer =>
        {
            var data = writer.BeginNested(4);
            writer.PutBytes(2, [0, 0, 0, 9]);
            writer.EndNested(data);
        });

        Assert.Null(NetfilterCounter.Packets([]));
        Assert.Null(NetfilterCounter.Packets([bare]));
        Assert.Null(NetfilterCounter.Packets([cut]));
    }

    private static NetlinkMessage Answer(Action<NetlinkWriter> fill)
    {
        var writer = new NetlinkWriter();
        writer.Begin(NewObject, 0, 7, 1, 0);
        fill(writer);

        return new NetlinkMessage(NewObject, 0, 1, writer.ToArray().AsMemory(20));
    }

    private static byte[] Big(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);

        return bytes;
    }
}
