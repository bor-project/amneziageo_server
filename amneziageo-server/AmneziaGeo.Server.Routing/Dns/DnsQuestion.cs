using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Builds the questions the panel asks the name servers itself.
/// </summary>
public static class DnsQuestion
{
    /// <summary>
    /// Returns the packet that asks for the records of one kind a name carries.
    /// </summary>
    public static byte[] Packet(string name, DnsRecordType type, ushort id)
    {
        ArgumentNullException.ThrowIfNull(name);

        var labels = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var packet = new byte[DnsMessage.HeaderLength + labels.Sum(label => label.Length + 1) + 5];
        BinaryPrimitives.WriteUInt16BigEndian(packet, id);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), 0x0100);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 1);
        var at = DnsMessage.HeaderLength;
        foreach (var label in labels)
        {
            packet[at++] = (byte)label.Length;
            at += Encoding.ASCII.GetBytes(label, packet.AsSpan(at));
        }

        packet[at++] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(at), (ushort)type);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(at + 2), 1);

        return packet;
    }

    /// <summary>
    /// Returns the number a fresh question carries.
    /// </summary>
    public static ushort Id() => BinaryPrimitives.ReadUInt16BigEndian(RandomNumberGenerator.GetBytes(2));
}
