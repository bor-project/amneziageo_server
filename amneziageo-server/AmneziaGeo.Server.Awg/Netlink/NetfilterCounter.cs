using System.Buffers.Binary;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// Reads a named counter of the firewall over netlink.
/// </summary>
public static class NetfilterCounter
{
    private const int NetlinkNetfilter = 12;

    private const ushort GetObject = 0x0A13;

    private const ushort FlagRequest = 1;

    private const byte FamilyInet = 1;

    private const ushort ObjectTable = 1;

    private const ushort ObjectName = 2;

    private const ushort ObjectType = 3;

    private const ushort ObjectData = 4;

    private const uint TypeCounter = 1;

    private const ushort CounterPackets = 2;

    private const int NoSuch = 2;

    /// <summary>
    /// Returns how many packets a counter of an inet table has seen, null when the host holds no such counter.
    /// </summary>
    public static long? Read(string table, string name)
    {
        using var socket = new NetlinkSocket(NetlinkNetfilter);
        try
        {
            return Packets(socket.Request(Request(socket.NextSequence(), table, name)));
        }
        catch (NetlinkException ex) when (ex.Error == NoSuch)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the request for a counter of an inet table.
    /// </summary>
    public static byte[] Request(uint sequence, string table, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(table);
        ArgumentException.ThrowIfNullOrEmpty(name);

        Span<byte> kind = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(kind, TypeCounter);
        var writer = new NetlinkWriter();
        writer.Begin(GetObject, FlagRequest, sequence, FamilyInet, 0);
        writer.PutString(ObjectTable, table);
        writer.PutString(ObjectName, name);
        writer.PutBytes(ObjectType, kind);

        return writer.ToArray();
    }

    /// <summary>
    /// Returns how many packets the counter of an answer has seen, null when the answer carries none.
    /// </summary>
    public static long? Packets(IEnumerable<NetlinkMessage> answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        foreach (var message in answer)
        {
            if (NetlinkAttributes.Map(message.Payload).TryGetValue(ObjectData, out var data)
                && NetlinkAttributes.Map(data).TryGetValue(CounterPackets, out var packets)
                && packets.Length >= 8)
            {
                return (long)BinaryPrimitives.ReadUInt64BigEndian(packets.Span);
            }
        }

        return null;
    }
}
