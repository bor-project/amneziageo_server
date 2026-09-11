using System.Net;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Routing.Outbound;

/// <summary>
/// Turns the settings of an outbound into a change for the kernel.
/// </summary>
public static class OutboundDevice
{
    /// <summary>
    /// The ranges the tunnel takes from the host.
    /// </summary>
    public static readonly string[] AllowedIps = ["0.0.0.0/0", "::/0"];

    /// <summary>
    /// Returns the change that puts the settings of an outbound on its interface.
    /// </summary>
    public static AwgUpdate Update(OutboundConfig outbound, IPEndPoint server)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        ArgumentNullException.ThrowIfNull(server);

        return new AwgUpdate
        {
            Name = outbound.Name,
            PrivateKey = outbound.PrivateKey,
            ReplacePeers = true,
            Obfuscation = OutboundRules.IsPlain(outbound.Obfuscation)
                ? null
                : ConfigDevice.Obfuscation(outbound.Obfuscation),
            Peers = [Peer(outbound, server)],
        };
    }

    /// <summary>
    /// Returns the server of an outbound as a peer of its interface.
    /// </summary>
    public static AwgPeerUpdate Peer(OutboundConfig outbound, IPEndPoint server)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        ArgumentNullException.ThrowIfNull(server);

        return new AwgPeerUpdate
        {
            PublicKey = outbound.PeerKey,
            PresharedKey = outbound.PresharedKey.Length > 0 ? outbound.PresharedKey : null,
            Endpoint = server,
            PersistentKeepalive = new AwgRange((uint)outbound.Keepalive),
            ReplaceAllowedIps = true,
            AllowedIps = [.. AllowedIps.Select(AwgAllowedIp.Parse)],
        };
    }

    /// <summary>
    /// Returns the addresses of the interface, each standing alone without its network.
    /// </summary>
    public static IReadOnlyList<string> Hosts(IReadOnlyList<string> address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return
        [
            .. address
                .Select(one => AwgAllowedIp.TryParse(one, out var found) ? found : null)
                .OfType<AwgAllowedIp>()
                .Select(found => new AwgAllowedIp(found.Address, found.IsSix ? (byte)128 : (byte)32).ToString()),
        ];
    }

    /// <summary>
    /// Returns what the host holds for an outbound, reading it off the interface.
    /// </summary>
    public static OutboundState State(OutboundConfig outbound, AwgDevice? device, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        if (device is null)
        {
            return OutboundState.Missing(outbound.Name);
        }

        var peer = device.Peers.FirstOrDefault(one => one.PublicKey == outbound.PeerKey) ?? device.Peers.FirstOrDefault();

        return new OutboundState(
            outbound.Name,
            true,
            peer?.Endpoint?.ToString() ?? string.Empty,
            peer?.LastHandshake,
            peer?.RxBytes ?? 0,
            peer?.TxBytes ?? 0,
            Alive(peer?.LastHandshake, now),
            string.Empty);
    }

    /// <summary>
    /// Tells whether a handshake is recent enough for the tunnel to carry traffic.
    /// </summary>
    public static bool Alive(DateTimeOffset? handshake, DateTimeOffset now) =>
        handshake is { } stamp && now - stamp < OutboundState.AliveFor;
}
