using System.Buffers.Binary;
using System.Net;

namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// Where a logged packet came from and where it went.
/// </summary>
/// <param name="Source">The address of the client.</param>
/// <param name="Target">The address the client went to.</param>
/// <param name="Protocol">The number of the transport protocol.</param>
/// <param name="Port">The port the client went to, 0 when the protocol carries none.</param>
/// <param name="SourcePort">The port the client went from, 0 when the protocol carries none.</param>
public sealed record AccessFlow(IPAddress Source, IPAddress Target, int Protocol, int Port, int SourcePort)
{
    /// <summary>
    /// The number of TCP.
    /// </summary>
    public const int Tcp = 6;

    /// <summary>
    /// The number of UDP.
    /// </summary>
    public const int Udp = 17;

    private const int V4Head = 20;
    private const int V6Head = 40;

    /// <summary>
    /// Reads the head of a packet, or returns null when it is not an IP packet.
    /// </summary>
    public static AccessFlow? Read(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            return null;
        }

        return (packet[0] >> 4) switch
        {
            4 => Four(packet),
            6 => Six(packet),
            _ => null,
        };
    }

    /// <summary>
    /// Returns where the transport header of a packet starts, or -1 when the packet is not an IP packet.
    /// </summary>
    public static int Transport(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            return -1;
        }

        return (packet[0] >> 4) switch
        {
            4 when packet.Length >= V4Head && (packet[0] & 0x0F) * 4 >= V4Head => (packet[0] & 0x0F) * 4,
            6 when packet.Length >= V6Head => V6Head,
            _ => -1,
        };
    }

    private static AccessFlow? Four(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < V4Head)
        {
            return null;
        }

        var head = (packet[0] & 0x0F) * 4;
        if (head < V4Head || packet.Length < head)
        {
            return null;
        }

        var protocol = packet[9];
        var later = (BinaryPrimitives.ReadUInt16BigEndian(packet[6..]) & 0x1FFF) != 0;
        var (from, to) = later ? (0, 0) : Ports(protocol, packet[head..]);

        return new AccessFlow(
            new IPAddress(packet.Slice(12, 4)),
            new IPAddress(packet.Slice(16, 4)),
            protocol,
            to,
            from);
    }

    private static AccessFlow? Six(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < V6Head)
        {
            return null;
        }

        var protocol = packet[6];
        var (from, to) = Ports(protocol, packet[V6Head..]);

        return new AccessFlow(
            new IPAddress(packet.Slice(8, 16)),
            new IPAddress(packet.Slice(24, 16)),
            protocol,
            to,
            from);
    }

    private static (int From, int To) Ports(int protocol, ReadOnlySpan<byte> transport) =>
        protocol is Tcp or Udp or 33 or 132 or 136 && transport.Length >= 4
            ? (BinaryPrimitives.ReadUInt16BigEndian(transport), BinaryPrimitives.ReadUInt16BigEndian(transport[2..]))
            : (0, 0);
}
