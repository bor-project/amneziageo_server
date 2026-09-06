using System.Net;

namespace AmneziaGeo.Server.Awg.Device;

/// <summary>
/// One peer of an interface as the kernel holds it.
/// </summary>
public sealed record AwgPeer
{
    /// <summary>
    /// The public key of the peer, in base64.
    /// </summary>
    public required string PublicKey { get; init; }

    /// <summary>
    /// The shared secret added on top of the handshake, in base64.
    /// </summary>
    public string? PresharedKey { get; init; }

    /// <summary>
    /// The address the last packet of the peer came from.
    /// </summary>
    public IPEndPoint? Endpoint { get; init; }

    /// <summary>
    /// How often an empty packet is sent to hold the path open, in seconds.
    /// </summary>
    public AwgRange PersistentKeepalive { get; init; }

    /// <summary>
    /// When the peer last completed a handshake.
    /// </summary>
    public DateTimeOffset? LastHandshake { get; init; }

    /// <summary>
    /// Bytes taken from the peer.
    /// </summary>
    public ulong RxBytes { get; init; }

    /// <summary>
    /// Bytes given to the peer.
    /// </summary>
    public ulong TxBytes { get; init; }

    /// <summary>
    /// The protocol version the kernel reports for the peer.
    /// </summary>
    public uint ProtocolVersion { get; init; }

    /// <summary>
    /// Whether the peer carries the advanced security of AmneziaWG.
    /// </summary>
    public bool AdvancedSecurity { get; init; }

    /// <summary>
    /// The address ranges routed to the peer.
    /// </summary>
    public IReadOnlyList<AwgAllowedIp> AllowedIps { get; init; } = [];
}
