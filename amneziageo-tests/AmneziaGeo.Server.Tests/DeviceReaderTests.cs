using System.Buffers.Binary;
using System.Net;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Awg.Uapi;

namespace AmneziaGeo.Server.Tests;

public class DeviceReaderTests
{
    private const ushort Family = 41;

    [Fact]
    public void TheHeadOfADumpGivesTheInterface()
    {
        var device = DeviceReader.Read([Message(writer =>
        {
            writer.PutString((ushort)WgDeviceAttribute.Ifname, "awg1");
            writer.PutU32((ushort)WgDeviceAttribute.Ifindex, 7);
            writer.PutU16((ushort)WgDeviceAttribute.ListenPort, 443);
            writer.PutU32((ushort)WgDeviceAttribute.Fwmark, 51820);
            writer.PutBytes((ushort)WgDeviceAttribute.PrivateKey, Key(1));
            writer.PutBytes((ushort)WgDeviceAttribute.PublicKey, Key(2));
        })]);

        Assert.NotNull(device);
        Assert.Equal("awg1", device.Name);
        Assert.Equal(7u, device.Index);
        Assert.Equal(443, device.ListenPort);
        Assert.Equal(51820u, device.Fwmark);
        Assert.Equal(Convert.ToBase64String(Key(1)), device.PrivateKey);
        Assert.Equal(Convert.ToBase64String(Key(2)), device.PublicKey);
    }

    [Fact]
    public void TheObfuscationIsReadAsSpansTheKernelPacked()
    {
        var device = DeviceReader.Read([Message(writer =>
        {
            writer.PutString((ushort)WgDeviceAttribute.Ifname, "awg1");
            writer.PutU16((ushort)WgDeviceAttribute.Jc, 4);
            writer.PutU16((ushort)WgDeviceAttribute.Jmin, 40);
            writer.PutU16((ushort)WgDeviceAttribute.Jmax, 70);
            writer.PutU16((ushort)WgDeviceAttribute.S1, 15);
            writer.PutU64((ushort)WgDeviceAttribute.H1, new AwgRange(1, 3).ToWide());
            writer.PutU32((ushort)WgDeviceAttribute.RekeyAfterTime, new AwgRange(110, 130).ToNarrow());
            writer.PutString((ushort)WgDeviceAttribute.I1, "<b 0xf1a2><c>");
            writer.PutU8((ushort)WgDeviceAttribute.RandomTrailers, 1);
            writer.PutU8((ushort)WgDeviceAttribute.DisableCookies, 0);
        })]);

        Assert.NotNull(device);
        var shape = device.Obfuscation;
        Assert.Equal(4, shape.Jc);
        Assert.Equal(40, shape.Jmin);
        Assert.Equal(70, shape.Jmax);
        Assert.Equal(15, shape.S1);
        Assert.Equal(new AwgRange(1, 3), shape.H1);
        Assert.Equal(new AwgRange(110, 130), shape.RekeyAfterTime);
        Assert.Equal("<b 0xf1a2><c>", shape.I1);
        Assert.True(shape.RandomTrailers);
        Assert.False(shape.DisableCookies);
    }

    [Fact]
    public void APeerCarriesItsCountersAndRanges()
    {
        var device = DeviceReader.Read([Message(writer =>
        {
            writer.PutString((ushort)WgDeviceAttribute.Ifname, "awg1");
            Peer(writer, Key(3), peer =>
            {
                peer.PutBytes((ushort)WgPeerAttribute.PresharedKey, Key(4));
                peer.PutBytes((ushort)WgPeerAttribute.Endpoint, PointV4("203.0.113.7", 51821));
                peer.PutU32((ushort)WgPeerAttribute.PersistentKeepaliveInterval, new AwgRange(25).ToNarrow());
                peer.PutBytes((ushort)WgPeerAttribute.LastHandshakeTime, Moment(1700000000, 500));
                peer.PutU64((ushort)WgPeerAttribute.RxBytes, 4096);
                peer.PutU64((ushort)WgPeerAttribute.TxBytes, 8192);
                peer.PutU32((ushort)WgPeerAttribute.Flags, (uint)WgPeerFlag.HasAdvancedSecurity);
                Ranges(peer, [new AwgAllowedIp(IPAddress.Parse("10.8.0.2"), 32)]);
            });
        })]);

        Assert.NotNull(device);
        var found = Assert.Single(device.Peers);
        Assert.Equal(Convert.ToBase64String(Key(3)), found.PublicKey);
        Assert.Equal(Convert.ToBase64String(Key(4)), found.PresharedKey);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.7"), 51821), found.Endpoint);
        Assert.Equal(new AwgRange(25), found.PersistentKeepalive);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).AddTicks(5), found.LastHandshake);
        Assert.Equal(4096ul, found.RxBytes);
        Assert.Equal(8192ul, found.TxBytes);
        Assert.True(found.AdvancedSecurity);
        Assert.Equal("10.8.0.2/32", Assert.Single(found.AllowedIps).ToString());
    }

    [Fact]
    public void APeerSplitAcrossMessagesIsJoined()
    {
        var head = Message(writer =>
        {
            writer.PutString((ushort)WgDeviceAttribute.Ifname, "awg1");
            Peer(writer, Key(3), peer => Ranges(peer, [new AwgAllowedIp(IPAddress.Parse("10.8.0.2"), 32)]));
        });

        var tail = Message(writer =>
        {
            writer.PutString((ushort)WgDeviceAttribute.Ifname, "awg1");
            Peer(writer, Key(3), peer => Ranges(peer, [new AwgAllowedIp(IPAddress.Parse("10.8.0.3"), 32)]));
        });

        var device = DeviceReader.Read([head, tail]);

        Assert.NotNull(device);
        var found = Assert.Single(device.Peers);
        Assert.Equal(["10.8.0.2/32", "10.8.0.3/32"], found.AllowedIps.Select(item => item.ToString()));
    }

    [Fact]
    public void PeersOfLaterMessagesAreAddedToTheFirstOnes()
    {
        var head = Message(writer =>
        {
            writer.PutString((ushort)WgDeviceAttribute.Ifname, "awg1");
            Peer(writer, Key(3), _ => { });
        });

        var tail = Message(writer =>
        {
            writer.PutString((ushort)WgDeviceAttribute.Ifname, "awg1");
            Peer(writer, Key(5), _ => { });
        });

        var device = DeviceReader.Read([head, tail]);

        Assert.NotNull(device);
        Assert.Equal(2, device.Peers.Count);
    }

    [Fact]
    public void AnEndpointOfTheSixthVersionIsRead()
    {
        var device = DeviceReader.Read([Message(writer =>
        {
            writer.PutString((ushort)WgDeviceAttribute.Ifname, "awg1");
            Peer(writer, Key(3), peer => peer.PutBytes((ushort)WgPeerAttribute.Endpoint, PointV6("2001:db8::7", 443)));
        })]);

        Assert.NotNull(device);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("2001:db8::7"), 443), Assert.Single(device.Peers).Endpoint);
    }

    [Fact]
    public void AnEmptyPresharedKeyIsNoKey()
    {
        var device = DeviceReader.Read([Message(writer =>
        {
            writer.PutString((ushort)WgDeviceAttribute.Ifname, "awg1");
            Peer(writer, Key(3), peer =>
            {
                peer.PutBytes((ushort)WgPeerAttribute.PresharedKey, new byte[WgUapi.KeyLength]);
                peer.PutBytes((ushort)WgPeerAttribute.LastHandshakeTime, Moment(0, 0));
            });
        })]);

        Assert.NotNull(device);
        var found = Assert.Single(device.Peers);
        Assert.Null(found.PresharedKey);
        Assert.Null(found.LastHandshake);
    }

    [Fact]
    public void ADumpWithoutANameIsNothing()
    {
        Assert.Null(DeviceReader.Read([]));
    }

    private static NetlinkMessage Message(Action<NetlinkWriter> body)
    {
        var writer = new NetlinkWriter();
        writer.Begin(Family, 0, 1, (byte)WgCmd.GetDevice, WgUapi.FamilyVersion);
        body(writer);

        return new NetlinkMessage(Family, 0, (byte)WgCmd.GetDevice, writer.ToArray().AsMemory(20));
    }

    private static void Peer(NetlinkWriter writer, byte[] key, Action<NetlinkWriter> body)
    {
        var peers = writer.BeginNested((ushort)WgDeviceAttribute.Peers);
        var one = writer.BeginNested(0);
        writer.PutBytes((ushort)WgPeerAttribute.PublicKey, key);
        body(writer);
        writer.EndNested(one);
        writer.EndNested(peers);
    }

    private static void Ranges(NetlinkWriter writer, IReadOnlyList<AwgAllowedIp> ranges)
    {
        var all = writer.BeginNested((ushort)WgPeerAttribute.Allowedips);
        foreach (var range in ranges)
        {
            var one = writer.BeginNested(0);
            writer.PutU16((ushort)WgAllowedIpAttribute.Family, 2);
            writer.PutBytes((ushort)WgAllowedIpAttribute.IpAddr, range.Address.GetAddressBytes());
            writer.PutU8((ushort)WgAllowedIpAttribute.CidrMask, range.Cidr);
            writer.EndNested(one);
        }

        writer.EndNested(all);
    }

    private static byte[] Key(byte seed)
    {
        var key = new byte[WgUapi.KeyLength];
        key[0] = seed;

        return key;
    }

    private static byte[] Moment(long seconds, long nanoseconds)
    {
        var stamp = new byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(stamp, seconds);
        BinaryPrimitives.WriteInt64LittleEndian(stamp.AsSpan(8), nanoseconds);

        return stamp;
    }

    private static byte[] PointV4(string address, ushort port)
    {
        var point = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(point, 2);
        BinaryPrimitives.WriteUInt16BigEndian(point.AsSpan(2), port);
        IPAddress.Parse(address).GetAddressBytes().CopyTo(point.AsSpan(4));

        return point;
    }

    private static byte[] PointV6(string address, ushort port)
    {
        var point = new byte[28];
        BinaryPrimitives.WriteUInt16LittleEndian(point, 10);
        BinaryPrimitives.WriteUInt16BigEndian(point.AsSpan(2), port);
        IPAddress.Parse(address).GetAddressBytes().CopyTo(point.AsSpan(8));

        return point;
    }
}
