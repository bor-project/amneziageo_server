using System.Buffers.Binary;
using System.Net;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Awg.Uapi;

namespace AmneziaGeo.Server.Awg.Device;

/// <summary>
/// Turns the messages of a device dump into one interface.
/// </summary>
public static class DeviceReader
{
    private const ushort AddressFamilyV4 = 2;
    private const ushort AddressFamilyV6 = 10;
    private const int TimespecLength = 16;
    private const int EndpointV6Length = 24;

    private static readonly string Blank = Convert.ToBase64String(new byte[WgUapi.KeyLength]);

    /// <summary>
    /// Reads a dump, joining peers and ranges the kernel split across messages.
    /// </summary>
    public static AwgDevice? Read(IEnumerable<NetlinkMessage> messages)
    {
        var draft = new Draft();
        foreach (var message in messages)
        {
            Absorb(draft, message.Payload);
        }

        return draft.Name is null ? null : draft.Build(draft.Name);
    }

    private static void Absorb(Draft draft, ReadOnlyMemory<byte> payload)
    {
        var map = NetlinkAttributes.Map(payload);

        draft.Name = NetlinkAttributes.Text(map, (ushort)WgDeviceAttribute.Ifname) ?? draft.Name;
        draft.Index = (uint?)NetlinkAttributes.Number(map, (ushort)WgDeviceAttribute.Ifindex) ?? draft.Index;
        draft.ListenPort = (ushort?)NetlinkAttributes.Number(map, (ushort)WgDeviceAttribute.ListenPort) ?? draft.ListenPort;
        draft.Fwmark = (uint?)NetlinkAttributes.Number(map, (ushort)WgDeviceAttribute.Fwmark) ?? draft.Fwmark;
        draft.PrivateKey = Key(map, (ushort)WgDeviceAttribute.PrivateKey) ?? draft.PrivateKey;
        draft.PublicKey = Key(map, (ushort)WgDeviceAttribute.PublicKey) ?? draft.PublicKey;

        if (map.ContainsKey((ushort)WgDeviceAttribute.Jc))
        {
            draft.Obfuscation = Shape(map);
        }

        if (!map.TryGetValue((ushort)WgDeviceAttribute.Peers, out var peers))
        {
            return;
        }

        foreach (var (_, value) in NetlinkAttributes.Walk(peers))
        {
            Peer(draft, value);
        }
    }

    private static AwgObfuscation Shape(Dictionary<ushort, ReadOnlyMemory<byte>> map) => new()
    {
        Jc = Small(map, WgDeviceAttribute.Jc),
        Jmin = Small(map, WgDeviceAttribute.Jmin),
        Jmax = Small(map, WgDeviceAttribute.Jmax),
        S1 = Small(map, WgDeviceAttribute.S1),
        S2 = Small(map, WgDeviceAttribute.S2),
        S3 = Small(map, WgDeviceAttribute.S3),
        S4 = Small(map, WgDeviceAttribute.S4),
        H1 = Wide(map, WgDeviceAttribute.H1),
        H2 = Wide(map, WgDeviceAttribute.H2),
        H3 = Wide(map, WgDeviceAttribute.H3),
        H4 = Wide(map, WgDeviceAttribute.H4),

        I1 = NetlinkAttributes.Text(map, (ushort)WgDeviceAttribute.I1),
        I2 = NetlinkAttributes.Text(map, (ushort)WgDeviceAttribute.I2),
        I3 = NetlinkAttributes.Text(map, (ushort)WgDeviceAttribute.I3),
        I4 = NetlinkAttributes.Text(map, (ushort)WgDeviceAttribute.I4),
        I5 = NetlinkAttributes.Text(map, (ushort)WgDeviceAttribute.I5),
        HeaderProtectionKey = Key(map, (ushort)WgDeviceAttribute.HeaderProtectionKey),
        ContentPaddingAddition = Narrow(map, WgDeviceAttribute.ContentPaddingAddition),
        RekeyAfterTime = Narrow(map, WgDeviceAttribute.RekeyAfterTime),
        RekeyTimeout = Narrow(map, WgDeviceAttribute.RekeyTimeout),
        RejectAfterTime = Narrow(map, WgDeviceAttribute.RejectAfterTime),
        KeepaliveTimeout = Narrow(map, WgDeviceAttribute.KeepaliveTimeout),
        MaxHandshakeAttempts = Narrow(map, WgDeviceAttribute.MaxHandshakeAttempts),
        RandomTrailers = Plain(map, WgDeviceAttribute.RandomTrailers) != 0,
        DisableCookies = Plain(map, WgDeviceAttribute.DisableCookies) != 0,
    };

    private static void Peer(Draft draft, ReadOnlyMemory<byte> payload)
    {
        var map = NetlinkAttributes.Map(payload);
        var key = Key(map, (ushort)WgPeerAttribute.PublicKey);
        if (key is null)
        {
            return;
        }

        var peer = draft.Peer(key);
        peer.PresharedKey = Secret(map, (ushort)WgPeerAttribute.PresharedKey) ?? peer.PresharedKey;
        peer.Endpoint = Endpoint(map) ?? peer.Endpoint;
        peer.PersistentKeepalive = Keepalive(map) ?? peer.PersistentKeepalive;
        peer.LastHandshake = Handshake(map) ?? peer.LastHandshake;
        peer.RxBytes = NetlinkAttributes.Number(map, (ushort)WgPeerAttribute.RxBytes) ?? peer.RxBytes;
        peer.TxBytes = NetlinkAttributes.Number(map, (ushort)WgPeerAttribute.TxBytes) ?? peer.TxBytes;
        peer.ProtocolVersion = (uint?)NetlinkAttributes.Number(map, (ushort)WgPeerAttribute.ProtocolVersion) ?? peer.ProtocolVersion;
        peer.AdvancedSecurity = peer.AdvancedSecurity || Advanced(map);

        if (!map.TryGetValue((ushort)WgPeerAttribute.Allowedips, out var ranges))
        {
            return;
        }

        foreach (var (_, value) in NetlinkAttributes.Walk(ranges))
        {
            var range = Range(NetlinkAttributes.Map(value));
            if (range is not null)
            {
                peer.AllowedIps.Add(range);
            }
        }
    }

    private static AwgRange? Keepalive(Dictionary<ushort, ReadOnlyMemory<byte>> map) =>
        NetlinkAttributes.Number(map, (ushort)WgPeerAttribute.PersistentKeepaliveInterval) is { } packed
            ? AwgRange.Narrow((uint)packed)
            : null;

    private static bool Advanced(Dictionary<ushort, ReadOnlyMemory<byte>> map)
    {
        if (map.ContainsKey((ushort)WgPeerAttribute.AdvancedSecurity))
        {
            return true;
        }

        var flags = (WgPeerFlag)(NetlinkAttributes.Number(map, (ushort)WgPeerAttribute.Flags) ?? 0);

        return flags.HasFlag(WgPeerFlag.HasAdvancedSecurity);
    }

    private static AwgAllowedIp? Range(Dictionary<ushort, ReadOnlyMemory<byte>> map)
    {
        if (!map.TryGetValue((ushort)WgAllowedIpAttribute.IpAddr, out var address) || address.Length is not 4 and not 16)
        {
            return null;
        }

        var cidr = map.TryGetValue((ushort)WgAllowedIpAttribute.CidrMask, out var mask) && mask.Length > 0
            ? mask.Span[0]
            : (byte)0;

        return new AwgAllowedIp(new IPAddress(address.Span), cidr);
    }

    private static IPEndPoint? Endpoint(Dictionary<ushort, ReadOnlyMemory<byte>> map)
    {
        if (!map.TryGetValue((ushort)WgPeerAttribute.Endpoint, out var value) || value.Length < 8)
        {
            return null;
        }

        var span = value.Span;
        var family = BinaryPrimitives.ReadUInt16LittleEndian(span);
        var port = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);

        if (family == AddressFamilyV4)
        {
            return new IPEndPoint(new IPAddress(span.Slice(4, 4)), port);
        }

        if (family == AddressFamilyV6 && span.Length >= EndpointV6Length)
        {
            return new IPEndPoint(new IPAddress(span.Slice(8, 16)), port);
        }

        return null;
    }

    private static DateTimeOffset? Handshake(Dictionary<ushort, ReadOnlyMemory<byte>> map)
    {
        if (!map.TryGetValue((ushort)WgPeerAttribute.LastHandshakeTime, out var value) || value.Length < TimespecLength)
        {
            return null;
        }

        var span = value.Span;
        var seconds = BinaryPrimitives.ReadInt64LittleEndian(span);
        var nanoseconds = BinaryPrimitives.ReadInt64LittleEndian(span[8..]);
        if (seconds <= 0 && nanoseconds <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanoseconds / 100);
    }

    private static string? Key(Dictionary<ushort, ReadOnlyMemory<byte>> map, ushort type) =>
        map.TryGetValue(type, out var value) && value.Length == WgUapi.KeyLength
            ? Convert.ToBase64String(value.Span)
            : null;

    private static string? Secret(Dictionary<ushort, ReadOnlyMemory<byte>> map, ushort type) =>
        Key(map, type) is { } key && !string.Equals(key, Blank, StringComparison.Ordinal) ? key : null;

    private static ushort Small(Dictionary<ushort, ReadOnlyMemory<byte>> map, WgDeviceAttribute type) =>
        (ushort)(NetlinkAttributes.Number(map, (ushort)type) ?? 0);

    private static uint Plain(Dictionary<ushort, ReadOnlyMemory<byte>> map, WgDeviceAttribute type) =>
        (uint)(NetlinkAttributes.Number(map, (ushort)type) ?? 0);

    private static AwgRange Wide(Dictionary<ushort, ReadOnlyMemory<byte>> map, WgDeviceAttribute type) =>
        AwgRange.Wide(NetlinkAttributes.Number(map, (ushort)type) ?? 0);

    private static AwgRange Narrow(Dictionary<ushort, ReadOnlyMemory<byte>> map, WgDeviceAttribute type) =>
        AwgRange.Narrow(Plain(map, type));

    private sealed class Draft
    {
        private readonly Dictionary<string, PeerDraft> _known = new(StringComparer.Ordinal);

        private readonly List<PeerDraft> _peers = [];

        public string? Name { get; set; }

        public uint Index { get; set; }

        public ushort ListenPort { get; set; }

        public uint Fwmark { get; set; }

        public string? PrivateKey { get; set; }

        public string? PublicKey { get; set; }

        public AwgObfuscation Obfuscation { get; set; } = new();

        public PeerDraft Peer(string key)
        {
            if (_known.TryGetValue(key, out var found))
            {
                return found;
            }

            var peer = new PeerDraft(key);
            _known[key] = peer;
            _peers.Add(peer);

            return peer;
        }

        public AwgDevice Build(string name) => new()
        {
            Name = name,
            Index = Index,
            ListenPort = ListenPort,
            Fwmark = Fwmark,
            PrivateKey = PrivateKey,
            PublicKey = PublicKey,
            Obfuscation = Obfuscation,
            Peers = _peers.Select(item => item.Build()).ToArray(),
        };
    }

    private sealed class PeerDraft(string publicKey)
    {
        public List<AwgAllowedIp> AllowedIps { get; } = [];

        public string? PresharedKey { get; set; }

        public IPEndPoint? Endpoint { get; set; }

        public AwgRange PersistentKeepalive { get; set; }

        public DateTimeOffset? LastHandshake { get; set; }

        public ulong RxBytes { get; set; }

        public ulong TxBytes { get; set; }

        public uint ProtocolVersion { get; set; }

        public bool AdvancedSecurity { get; set; }

        public AwgPeer Build() => new()
        {
            PublicKey = publicKey,
            PresharedKey = PresharedKey,
            Endpoint = Endpoint,
            PersistentKeepalive = PersistentKeepalive,
            LastHandshake = LastHandshake,
            RxBytes = RxBytes,
            TxBytes = TxBytes,
            ProtocolVersion = ProtocolVersion,
            AdvancedSecurity = AdvancedSecurity,
            AllowedIps = AllowedIps.ToArray(),
        };
    }
}
