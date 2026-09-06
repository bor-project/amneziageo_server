using System.Buffers.Binary;
using System.Net;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Awg.Uapi;

namespace AmneziaGeo.Server.Tests;

public class DeviceWriterTests
{
    private const ushort Family = 41;

    [Fact]
    public void AChangeWithoutPeersIsOneRequest()
    {
        var requests = DeviceWriter.Requests(
            new AwgUpdate { Name = "awg1", PrivateKey = Text(1), ListenPort = 443, Fwmark = 51820 },
            Family,
            Sequence());

        var map = Attributes(Assert.Single(requests));

        Assert.Equal("awg1", NetlinkAttributes.Text(map, (ushort)WgDeviceAttribute.Ifname));
        Assert.Equal((ushort?)443, NetlinkAttributes.U16(map, (ushort)WgDeviceAttribute.ListenPort));
        Assert.Equal((uint?)51820, NetlinkAttributes.U32(map, (ushort)WgDeviceAttribute.Fwmark));
        Assert.Equal(Text(1), Convert.ToBase64String(map[(ushort)WgDeviceAttribute.PrivateKey].Span));
    }

    [Fact]
    public void ReplacingThePeersIsAskedForOnlyOnce()
    {
        var requests = DeviceWriter.Requests(
            new AwgUpdate { Name = "awg1", ReplacePeers = true, Peers = Many(400) },
            Family,
            Sequence());

        Assert.True(requests.Count > 1);
        Assert.Equal((uint?)WgDeviceFlag.ReplacePeers, NetlinkAttributes.U32(Attributes(requests[0]), (ushort)WgDeviceAttribute.Flags));

        foreach (var request in requests.Skip(1))
        {
            var map = Attributes(request);
            Assert.Equal("awg1", NetlinkAttributes.Text(map, (ushort)WgDeviceAttribute.Ifname));
            Assert.False(map.ContainsKey((ushort)WgDeviceAttribute.Flags));
        }
    }

    [Fact]
    public void EveryPeerOfALongListIsWrittenOnce()
    {
        var requests = DeviceWriter.Requests(
            new AwgUpdate { Name = "awg1", Peers = Many(400) },
            Family,
            Sequence());

        var written = requests.SelectMany(Peers).ToArray();

        Assert.Equal(400, written.Length);
        Assert.Equal(400, written.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheRangesOfOnePeerAreSplitUnderTheSameKey()
    {
        var ranges = Enumerable.Range(0, 200)
            .Select(step => new AwgAllowedIp(IPAddress.Parse($"10.{step / 256}.{step % 256}.0"), 24))
            .ToArray();

        var requests = DeviceWriter.Requests(
            new AwgUpdate
            {
                Name = "awg1",
                Peers = [new AwgPeerUpdate { PublicKey = Text(3), AllowedIps = ranges, ReplaceAllowedIps = true }],
            },
            Family,
            Sequence());

        var fragments = requests.SelectMany(Peers).ToArray();

        Assert.True(fragments.Length > 1);
        Assert.All(fragments, key => Assert.Equal(Text(3), key));
        Assert.Equal(200, requests.Sum(RangeCount));
        Assert.Equal(1, requests.Sum(FlagCount));
    }

    [Fact]
    public void AZeroPacketTypeIsLeftAlone()
    {
        var requests = DeviceWriter.Requests(
            new AwgUpdate { Name = "awg1", Obfuscation = new AwgObfuscation { Jc = 4, H2 = new AwgRange(4, 6) } },
            Family,
            Sequence());

        var map = Attributes(Assert.Single(requests));

        Assert.False(map.ContainsKey((ushort)WgDeviceAttribute.H1));
        Assert.Equal(new AwgRange(4, 6).ToWide(), NetlinkAttributes.U64(map, (ushort)WgDeviceAttribute.H2));
        Assert.Equal((ushort?)4, NetlinkAttributes.U16(map, (ushort)WgDeviceAttribute.Jc));
    }

    [Fact]
    public void RemovingAPeerWritesNothingButTheFlag()
    {
        var requests = DeviceWriter.Requests(
            new AwgUpdate
            {
                Name = "awg1",
                Peers =
                [
                    new AwgPeerUpdate
                    {
                        PublicKey = Text(3),
                        Remove = true,
                        Endpoint = new IPEndPoint(IPAddress.Loopback, 443),
                        AllowedIps = [new AwgAllowedIp(IPAddress.Loopback, 32)],
                    },
                ],
            },
            Family,
            Sequence());

        var peer = Assert.Single(Nested(Assert.Single(requests)));

        Assert.Equal((uint?)WgPeerFlag.RemoveMe, NetlinkAttributes.U32(peer, (ushort)WgPeerAttribute.Flags));
        Assert.False(peer.ContainsKey((ushort)WgPeerAttribute.Endpoint));
        Assert.False(peer.ContainsKey((ushort)WgPeerAttribute.Allowedips));
    }

    [Fact]
    public void AKeyOfTheWrongLengthIsRefused()
    {
        var update = new AwgUpdate { Name = "awg1", PrivateKey = Convert.ToBase64String(new byte[16]) };

        Assert.Throws<ArgumentException>(() => DeviceWriter.Requests(update, Family, Sequence()));
    }

    private static Func<uint> Sequence()
    {
        var next = 0u;

        return () => ++next;
    }

    private static IReadOnlyList<AwgPeerUpdate> Many(int count) =>
    [
        .. Enumerable.Range(1, count).Select(step => new AwgPeerUpdate
        {
            PublicKey = Numbered(step),
            AllowedIps = [new AwgAllowedIp(IPAddress.Parse($"10.{step / 256}.{step % 256}.1"), 32)],
        }),
    ];

    private static string Numbered(int step)
    {
        var key = new byte[WgUapi.KeyLength];
        BinaryPrimitives.WriteInt32LittleEndian(key, step);

        return Convert.ToBase64String(key);
    }

    private static string Text(byte seed)
    {
        var key = new byte[WgUapi.KeyLength];
        key[0] = seed;
        key[1] = (byte)(seed >> 1);

        return Convert.ToBase64String(key);
    }

    private static Dictionary<ushort, ReadOnlyMemory<byte>> Attributes(byte[] request) =>
        NetlinkAttributes.Map(request.AsMemory(20));

    private static IEnumerable<Dictionary<ushort, ReadOnlyMemory<byte>>> Nested(byte[] request)
    {
        var map = Attributes(request);
        if (!map.TryGetValue((ushort)WgDeviceAttribute.Peers, out var peers))
        {
            yield break;
        }

        foreach (var (_, value) in NetlinkAttributes.Walk(peers))
        {
            yield return NetlinkAttributes.Map(value);
        }
    }

    private static IEnumerable<string> Peers(byte[] request) =>
        Nested(request).Select(peer => Convert.ToBase64String(peer[(ushort)WgPeerAttribute.PublicKey].Span));

    private static int RangeCount(byte[] request) =>
        Nested(request).Sum(peer => peer.TryGetValue((ushort)WgPeerAttribute.Allowedips, out var ranges)
            ? NetlinkAttributes.Walk(ranges).Count()
            : 0);

    private static int FlagCount(byte[] request) =>
        Nested(request).Count(peer => peer.ContainsKey((ushort)WgPeerAttribute.Flags));
}
