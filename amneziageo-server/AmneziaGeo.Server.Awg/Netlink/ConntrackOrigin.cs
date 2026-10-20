using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// Reads from the connection tracking of the host where a connection turned to a local socket was first sent.
/// </summary>
public static class ConntrackOrigin
{
    private const int NetlinkNetfilter = 12;

    private const ushort GetConntrack = 0x0101;

    private const ushort FlagRequest = 1;

    private const ushort FlagAck = 4;

    private const byte FamilyIp4 = 2;

    private const byte FamilyIp6 = 10;

    private const ushort TupleOriginal = 1;

    private const ushort TupleReply = 2;

    private const ushort TupleIp = 1;

    private const ushort TupleProto = 2;

    private const ushort Ip4Source = 1;

    private const ushort Ip4Destination = 2;

    private const ushort Ip6Source = 3;

    private const ushort Ip6Destination = 4;

    private const ushort ProtoNumber = 1;

    private const ushort ProtoSourcePort = 2;

    private const ushort ProtoDestinationPort = 3;

    private const byte Tcp = 6;

    private const byte Udp = 17;

    /// <summary>
    /// Returns the address a connection from a client to a local socket was sent to, or null when the host tracks no
    /// such connection.
    /// </summary>
    public static IPAddress? Find(IPEndPoint client, IPEndPoint local, bool stream)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(local);

        if (client.AddressFamily != local.AddressFamily)
        {
            return null;
        }

        try
        {
            using var socket = new NetlinkSocket(NetlinkNetfilter);

            return Destination(socket.Request(Request(socket.NextSequence(), client, local, stream)));
        }
        catch (Exception ex) when (ex is NetlinkException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the request for the connection whose answers go from a local socket back to a client.
    /// </summary>
    public static byte[] Request(uint sequence, IPEndPoint client, IPEndPoint local, bool stream)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(local);

        var six = local.AddressFamily == AddressFamily.InterNetworkV6;
        var writer = new NetlinkWriter();
        writer.Begin(GetConntrack, FlagRequest | FlagAck, sequence, six ? FamilyIp6 : FamilyIp4, 0);
        var tuple = writer.BeginNested(TupleReply);
        var ip = writer.BeginNested(TupleIp);
        writer.PutBytes(six ? Ip6Source : Ip4Source, local.Address.GetAddressBytes());
        writer.PutBytes(six ? Ip6Destination : Ip4Destination, client.Address.GetAddressBytes());
        writer.EndNested(ip);
        var proto = writer.BeginNested(TupleProto);
        writer.PutU8(ProtoNumber, stream ? Tcp : Udp);
        writer.PutBytes(ProtoSourcePort, Port(local.Port));
        writer.PutBytes(ProtoDestinationPort, Port(client.Port));
        writer.EndNested(proto);
        writer.EndNested(tuple);

        return writer.ToArray();
    }

    /// <summary>
    /// Returns the address the original direction of the connection of an answer went to, or null when it carries none.
    /// </summary>
    public static IPAddress? Destination(IEnumerable<NetlinkMessage> answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        foreach (var message in answer)
        {
            if (NetlinkAttributes.Map(message.Payload).TryGetValue(TupleOriginal, out var tuple)
                && NetlinkAttributes.Map(tuple).TryGetValue(TupleIp, out var ip))
            {
                var addresses = NetlinkAttributes.Map(ip);
                if (addresses.TryGetValue(Ip4Destination, out var four) && four.Length == 4)
                {
                    return new IPAddress(four.Span);
                }

                if (addresses.TryGetValue(Ip6Destination, out var sixteen) && sixteen.Length == 16)
                {
                    return new IPAddress(sixteen.Span);
                }
            }
        }

        return null;
    }

    private static byte[] Port(int port)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)port);

        return bytes;
    }
}
