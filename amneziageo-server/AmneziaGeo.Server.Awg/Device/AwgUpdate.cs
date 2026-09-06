using System.Net;

namespace AmneziaGeo.Server.Awg.Device;

/// <summary>
/// A change to one interface, carrying only what is set.
/// </summary>
public sealed record AwgUpdate
{
    /// <summary>
    /// The name of the interface the change goes to.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The private key to put on the interface, in base64.
    /// </summary>
    public string? PrivateKey { get; init; }

    /// <summary>
    /// The port to take packets on.
    /// </summary>
    public ushort? ListenPort { get; init; }

    /// <summary>
    /// The mark to put on outgoing packets.
    /// </summary>
    public uint? Fwmark { get; init; }

    /// <summary>
    /// Whether the peers the interface holds are dropped before the ones below are added.
    /// </summary>
    public bool ReplacePeers { get; init; }

    /// <summary>
    /// The obfuscation to put on the interface.
    /// </summary>
    public AwgObfuscation? Obfuscation { get; init; }

    /// <summary>
    /// The peers to add, change or remove.
    /// </summary>
    public IReadOnlyList<AwgPeerUpdate> Peers { get; init; } = [];
}

/// <summary>
/// A change to one peer of an interface.
/// </summary>
public sealed record AwgPeerUpdate
{
    /// <summary>
    /// The public key of the peer, in base64.
    /// </summary>
    public required string PublicKey { get; init; }

    /// <summary>
    /// The shared secret to add on top of the handshake, in base64.
    /// </summary>
    public string? PresharedKey { get; init; }

    /// <summary>
    /// The address to send to until the peer speaks from another one.
    /// </summary>
    public IPEndPoint? Endpoint { get; init; }

    /// <summary>
    /// How often an empty packet holds the path open, in seconds.
    /// </summary>
    public AwgRange? PersistentKeepalive { get; init; }

    /// <summary>
    /// Whether the peer is taken off the interface.
    /// </summary>
    public bool Remove { get; init; }

    /// <summary>
    /// Whether the change is dropped when the interface does not already carry the peer.
    /// </summary>
    public bool UpdateOnly { get; init; }

    /// <summary>
    /// Whether the ranges the peer holds are dropped before the ones below are added.
    /// </summary>
    public bool ReplaceAllowedIps { get; init; }

    /// <summary>
    /// The address ranges routed to the peer.
    /// </summary>
    public IReadOnlyList<AwgAllowedIp> AllowedIps { get; init; } = [];
}
