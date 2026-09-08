using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Routing.Outbound;

/// <summary>
/// One way out of the host, as the panel holds it.
/// </summary>
public sealed record OutboundConfig
{
    /// <summary>
    /// The number the panel keeps the outbound under.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The name of the outbound, and of its interface when it carries one.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The way traffic leaves the host.
    /// </summary>
    public string Kind { get; init; } = OutboundKind.Local;

    /// <summary>
    /// Where the outbound stands among the others.
    /// </summary>
    public int Position { get; init; }

    /// <summary>
    /// Whether the outbound is put on the host.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// The address of the server the tunnel goes to.
    /// </summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// The port of the server the tunnel goes to.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// The private key of the interface, in base64.
    /// </summary>
    public string PrivateKey { get; init; } = string.Empty;

    /// <summary>
    /// The public key of the interface, in base64.
    /// </summary>
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>
    /// The public key of the server, in base64.
    /// </summary>
    public string PeerKey { get; init; } = string.Empty;

    /// <summary>
    /// The key added on top of the handshake, in base64.
    /// </summary>
    public string PresharedKey { get; init; } = string.Empty;

    /// <summary>
    /// The address ranges the interface carries.
    /// </summary>
    public IReadOnlyList<string> Address { get; init; } = [];

    /// <summary>
    /// The name servers reachable through the outbound.
    /// </summary>
    public IReadOnlyList<string> Dns { get; init; } = [];

    /// <summary>
    /// The packet size of the interface, zero when the kernel picks it.
    /// </summary>
    public int Mtu { get; init; }

    /// <summary>
    /// How often an empty packet holds the path to the server open, in seconds.
    /// </summary>
    public int Keepalive { get; init; }

    /// <summary>
    /// The obfuscation the tunnel carries.
    /// </summary>
    public ObfuscationSettings Obfuscation { get; init; } = new();

    /// <summary>
    /// The mark that sends a packet out through this outbound.
    /// </summary>
    public uint Mark { get; init; }

    /// <summary>
    /// The routing table the mark looks the way out up in.
    /// </summary>
    public int Table { get; init; }

    /// <summary>
    /// When the outbound was added.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// When the outbound was last changed.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }
}
