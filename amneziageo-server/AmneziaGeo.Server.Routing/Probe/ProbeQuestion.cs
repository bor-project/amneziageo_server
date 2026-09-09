using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AmneziaGeo.Server.Routing.Probe;

/// <summary>
/// Builds the question a probe asks and reads the answer back.
/// </summary>
public static class ProbeQuestion
{
    /// <summary>
    /// The length of the header every message carries.
    /// </summary>
    public const int HeaderSize = 12;

    /// <summary>
    /// The longest name a probe asks for.
    /// </summary>
    public const int MaxNameLength = 253;

    /// <summary>
    /// Returns the packet that asks a name server for the address of a name.
    /// </summary>
    public static byte[] Packet(string name, ushort id)
    {
        ArgumentNullException.ThrowIfNull(name);

        var labels = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var packet = new byte[HeaderSize + labels.Sum(label => label.Length + 1) + 5];
        BinaryPrimitives.WriteUInt16BigEndian(packet, id);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), 0x0100);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 1);
        var at = HeaderSize;
        foreach (var label in labels)
        {
            packet[at++] = (byte)label.Length;
            at += Encoding.ASCII.GetBytes(label, packet.AsSpan(at));
        }

        packet[at++] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(at), 1);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(at + 2), 1);

        return packet;
    }

    /// <summary>
    /// Returns the number a fresh question carries.
    /// </summary>
    public static ushort Id() => BinaryPrimitives.ReadUInt16BigEndian(RandomNumberGenerator.GetBytes(2));

    /// <summary>
    /// Tells whether a packet is the answer to the question of a probe.
    /// </summary>
    public static bool Answers(ReadOnlySpan<byte> packet, ushort id) =>
        packet.Length >= HeaderSize
        && BinaryPrimitives.ReadUInt16BigEndian(packet) == id
        && (packet[2] & 0x80) != 0;
}
