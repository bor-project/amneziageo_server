using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Awg.Uapi;

namespace AmneziaGeo.Server.Awg.Device;

/// <summary>
/// Lays a change out as the netlink requests the kernel takes.
/// </summary>
public static class DeviceWriter
{
    private const ushort FlagRequest = 1;
    private const ushort FlagAck = 4;
    private const ushort AddressFamilyV4 = 2;
    private const ushort AddressFamilyV6 = 10;
    private const int SockaddrV4Length = 16;
    private const int SockaddrV6Length = 28;
    private const int RangesPerFragment = 64;
    private const int MessageBudget = 6144;
    private const int PeerCost = 128;
    private const int RangeCost = 48;

    /// <summary>
    /// Lays a change out, splitting it into several requests when it outgrows one message.
    /// </summary>
    public static IReadOnlyList<byte[]> Requests(AwgUpdate update, ushort family, Func<uint> sequence)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(sequence);

        var chunks = Chunks(update.Peers);
        var requests = new List<byte[]>();

        for (var step = 0; step < chunks.Count; step++)
        {
            var writer = new NetlinkWriter();
            writer.Begin(family, FlagRequest | FlagAck, sequence(), (byte)WgCmd.SetDevice, WgUapi.FamilyVersion);
            writer.PutString((ushort)WgDeviceAttribute.Ifname, update.Name);

            if (step == 0)
            {
                Head(writer, update);
            }

            Peers(writer, chunks[step]);
            requests.Add(writer.ToArray());
        }

        return requests;
    }

    private static void Head(NetlinkWriter writer, AwgUpdate update)
    {
        if (update.ReplacePeers)
        {
            writer.PutU32((ushort)WgDeviceAttribute.Flags, (uint)WgDeviceFlag.ReplacePeers);
        }

        if (update.PrivateKey is not null)
        {
            writer.PutBytes((ushort)WgDeviceAttribute.PrivateKey, Key(update.PrivateKey));
        }

        if (update.ListenPort is { } port)
        {
            writer.PutU16((ushort)WgDeviceAttribute.ListenPort, port);
        }

        if (update.Fwmark is { } mark)
        {
            writer.PutU32((ushort)WgDeviceAttribute.Fwmark, mark);
        }

        if (update.Obfuscation is { } shape)
        {
            Shape(writer, shape);
        }
    }

    private static void Shape(NetlinkWriter writer, AwgObfuscation shape)
    {
        writer.PutU16((ushort)WgDeviceAttribute.Jc, shape.Jc);
        writer.PutU16((ushort)WgDeviceAttribute.Jmin, shape.Jmin);
        writer.PutU16((ushort)WgDeviceAttribute.Jmax, shape.Jmax);
        writer.PutU16((ushort)WgDeviceAttribute.S1, shape.S1);
        writer.PutU16((ushort)WgDeviceAttribute.S2, shape.S2);
        writer.PutU16((ushort)WgDeviceAttribute.S3, shape.S3);
        writer.PutU16((ushort)WgDeviceAttribute.S4, shape.S4);
        Header(writer, WgDeviceAttribute.H1, shape.H1);
        Header(writer, WgDeviceAttribute.H2, shape.H2);
        Header(writer, WgDeviceAttribute.H3, shape.H3);
        Header(writer, WgDeviceAttribute.H4, shape.H4);
        writer.PutU32((ushort)WgDeviceAttribute.ContentPaddingAddition, shape.ContentPaddingAddition.ToNarrow());
        writer.PutU32((ushort)WgDeviceAttribute.RekeyAfterTime, shape.RekeyAfterTime.ToNarrow());
        writer.PutU32((ushort)WgDeviceAttribute.RekeyTimeout, shape.RekeyTimeout.ToNarrow());
        writer.PutU32((ushort)WgDeviceAttribute.RejectAfterTime, shape.RejectAfterTime.ToNarrow());
        writer.PutU32((ushort)WgDeviceAttribute.KeepaliveTimeout, shape.KeepaliveTimeout.ToNarrow());
        writer.PutU32((ushort)WgDeviceAttribute.MaxHandshakeAttempts, shape.MaxHandshakeAttempts.ToNarrow());
        writer.PutU8((ushort)WgDeviceAttribute.RandomTrailers, shape.RandomTrailers ? (byte)1 : (byte)0);
        writer.PutU8((ushort)WgDeviceAttribute.DisableCookies, shape.DisableCookies ? (byte)1 : (byte)0);

        Junk(writer, WgDeviceAttribute.I1, shape.I1);
        Junk(writer, WgDeviceAttribute.I2, shape.I2);
        Junk(writer, WgDeviceAttribute.I3, shape.I3);
        Junk(writer, WgDeviceAttribute.I4, shape.I4);
        Junk(writer, WgDeviceAttribute.I5, shape.I5);

        if (shape.HeaderProtectionKey is not null)
        {
            writer.PutBytes((ushort)WgDeviceAttribute.HeaderProtectionKey, Key(shape.HeaderProtectionKey));
        }
    }

    private static void Header(NetlinkWriter writer, WgDeviceAttribute type, AwgRange range)
    {
        if (!range.IsZero)
        {
            writer.PutU64((ushort)type, range.ToWide());
        }
    }

    private static void Junk(NetlinkWriter writer, WgDeviceAttribute type, string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            writer.PutString((ushort)type, text);
        }
    }

    private static void Peers(NetlinkWriter writer, IReadOnlyList<Fragment> chunk)
    {
        if (chunk.Count == 0)
        {
            return;
        }

        var mark = writer.BeginNested((ushort)WgDeviceAttribute.Peers);
        foreach (var fragment in chunk)
        {
            Peer(writer, fragment);
        }

        writer.EndNested(mark);
    }

    private static void Peer(NetlinkWriter writer, Fragment fragment)
    {
        var peer = fragment.Peer;
        var mark = writer.BeginNested(0);
        writer.PutBytes((ushort)WgPeerAttribute.PublicKey, Key(peer.PublicKey));

        if (fragment.IsFirst)
        {
            var flags = Flags(peer);
            if (flags != 0)
            {
                writer.PutU32((ushort)WgPeerAttribute.Flags, (uint)flags);
            }

            if (peer.Remove)
            {
                writer.EndNested(mark);

                return;
            }

            if (peer.PresharedKey is not null)
            {
                writer.PutBytes((ushort)WgPeerAttribute.PresharedKey, Key(peer.PresharedKey));
            }

            if (peer.Endpoint is not null)
            {
                writer.PutBytes((ushort)WgPeerAttribute.Endpoint, Sockaddr(peer.Endpoint));
            }

            if (peer.PersistentKeepalive is { } keepalive)
            {
                writer.PutU32((ushort)WgPeerAttribute.PersistentKeepaliveInterval, keepalive.ToNarrow());
            }
        }

        if (fragment.Ranges.Count > 0)
        {
            var ranges = writer.BeginNested((ushort)WgPeerAttribute.Allowedips);
            foreach (var range in fragment.Ranges)
            {
                Range(writer, range);
            }

            writer.EndNested(ranges);
        }

        writer.EndNested(mark);
    }

    private static void Range(NetlinkWriter writer, AwgAllowedIp range)
    {
        var mark = writer.BeginNested(0);
        writer.PutU16((ushort)WgAllowedIpAttribute.Family,
            range.Address.AddressFamily == AddressFamily.InterNetworkV6 ? AddressFamilyV6 : AddressFamilyV4);
        writer.PutBytes((ushort)WgAllowedIpAttribute.IpAddr, range.Address.GetAddressBytes());
        writer.PutU8((ushort)WgAllowedIpAttribute.CidrMask, range.Cidr);
        writer.EndNested(mark);
    }

    private static WgPeerFlag Flags(AwgPeerUpdate peer)
    {
        var flags = default(WgPeerFlag);
        if (peer.Remove)
        {
            flags |= WgPeerFlag.RemoveMe;
        }

        if (peer.UpdateOnly)
        {
            flags |= WgPeerFlag.UpdateOnly;
        }

        if (peer.ReplaceAllowedIps)
        {
            flags |= WgPeerFlag.ReplaceAllowedips;
        }

        return flags;
    }

    private static byte[] Sockaddr(IPEndPoint point)
    {
        if (point.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var six = new byte[SockaddrV6Length];
            BinaryPrimitives.WriteUInt16LittleEndian(six, AddressFamilyV6);
            BinaryPrimitives.WriteUInt16BigEndian(six.AsSpan(2), (ushort)point.Port);
            point.Address.GetAddressBytes().CopyTo(six.AsSpan(8));
            BinaryPrimitives.WriteUInt32LittleEndian(six.AsSpan(24), (uint)point.Address.ScopeId);

            return six;
        }

        var four = new byte[SockaddrV4Length];
        BinaryPrimitives.WriteUInt16LittleEndian(four, AddressFamilyV4);
        BinaryPrimitives.WriteUInt16BigEndian(four.AsSpan(2), (ushort)point.Port);
        point.Address.GetAddressBytes().CopyTo(four.AsSpan(4));

        return four;
    }

    private static byte[] Key(string text)
    {
        var bytes = Convert.FromBase64String(text);

        return bytes.Length == WgUapi.KeyLength
            ? bytes
            : throw new ArgumentException($"a key takes {WgUapi.KeyLength} bytes, this one takes {bytes.Length}", nameof(text));
    }

    private static List<IReadOnlyList<Fragment>> Chunks(IReadOnlyList<AwgPeerUpdate> peers)
    {
        var chunks = new List<IReadOnlyList<Fragment>>();
        var current = new List<Fragment>();
        var room = MessageBudget;

        foreach (var fragment in Fragments(peers))
        {
            var cost = PeerCost + (fragment.Ranges.Count * RangeCost);
            if (current.Count > 0 && cost > room)
            {
                chunks.Add(current);
                current = [];
                room = MessageBudget;
            }

            current.Add(fragment);
            room -= cost;
        }

        chunks.Add(current);

        return chunks;
    }

    private static IEnumerable<Fragment> Fragments(IReadOnlyList<AwgPeerUpdate> peers)
    {
        foreach (var peer in peers)
        {
            var step = 0;
            var first = true;

            do
            {
                var ranges = peer.AllowedIps.Skip(step).Take(RangesPerFragment).ToArray();
                step += ranges.Length;

                yield return new Fragment(peer, ranges, first);

                first = false;
            }
            while (step < peer.AllowedIps.Count);
        }
    }

    private sealed record Fragment(AwgPeerUpdate Peer, IReadOnlyList<AwgAllowedIp> Ranges, bool IsFirst);
}
