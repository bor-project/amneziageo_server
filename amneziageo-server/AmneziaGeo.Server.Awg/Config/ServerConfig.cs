namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// The obfuscation an endpoint carries, as the panel holds it.
/// </summary>
public sealed record ObfuscationSettings
{
    /// <summary>
    /// How many junk packets go before a handshake.
    /// </summary>
    public int Jc { get; init; }

    /// <summary>
    /// The shortest junk packet, in bytes.
    /// </summary>
    public int Jmin { get; init; }

    /// <summary>
    /// The longest junk packet, in bytes.
    /// </summary>
    public int Jmax { get; init; }

    /// <summary>
    /// Junk prepended to a handshake initiation, in bytes.
    /// </summary>
    public int S1 { get; init; }

    /// <summary>
    /// Junk prepended to a handshake response, in bytes.
    /// </summary>
    public int S2 { get; init; }

    /// <summary>
    /// Junk prepended to a cookie reply, in bytes.
    /// </summary>
    public int S3 { get; init; }

    /// <summary>
    /// Junk prepended to a transport packet, in bytes.
    /// </summary>
    public int S4 { get; init; }

    /// <summary>
    /// The type written into a handshake initiation, one number or a span.
    /// </summary>
    public string H1 { get; init; } = string.Empty;

    /// <summary>
    /// The type written into a handshake response, one number or a span.
    /// </summary>
    public string H2 { get; init; } = string.Empty;

    /// <summary>
    /// The type written into a cookie reply, one number or a span.
    /// </summary>
    public string H3 { get; init; } = string.Empty;

    /// <summary>
    /// The type written into a transport packet, one number or a span.
    /// </summary>
    public string H4 { get; init; } = string.Empty;

    /// <summary>
    /// The first special junk packet.
    /// </summary>
    public string? I1 { get; init; }

    /// <summary>
    /// The second special junk packet.
    /// </summary>
    public string? I2 { get; init; }

    /// <summary>
    /// The third special junk packet.
    /// </summary>
    public string? I3 { get; init; }

    /// <summary>
    /// The fourth special junk packet.
    /// </summary>
    public string? I4 { get; init; }

    /// <summary>
    /// The fifth special junk packet.
    /// </summary>
    public string? I5 { get; init; }

    /// <summary>
    /// The key the packet header is hidden with, in base64.
    /// </summary>
    public string HeaderProtectionKey { get; init; } = string.Empty;

    /// <summary>
    /// Padding added to the content of a transport packet, in bytes.
    /// </summary>
    public string ContentPaddingAddition { get; init; } = string.Empty;

    /// <summary>
    /// When a session is renewed, in seconds.
    /// </summary>
    public string RekeyAfterTime { get; init; } = string.Empty;

    /// <summary>
    /// How long a handshake attempt waits before it repeats, in seconds.
    /// </summary>
    public string RekeyTimeout { get; init; } = string.Empty;

    /// <summary>
    /// When a session stops being accepted, in seconds.
    /// </summary>
    public string RejectAfterTime { get; init; } = string.Empty;

    /// <summary>
    /// How long a quiet path is held open, in seconds.
    /// </summary>
    public string KeepaliveTimeout { get; init; } = string.Empty;

    /// <summary>
    /// How many handshakes are attempted before the peer is given up on.
    /// </summary>
    public string MaxHandshakeAttempts { get; init; } = string.Empty;

    /// <summary>
    /// Whether random bytes are appended to packets.
    /// </summary>
    public bool RandomTrailers { get; init; }

    /// <summary>
    /// Whether cookie replies are turned off.
    /// </summary>
    public bool DisableCookies { get; init; }
}

/// <summary>
/// The settings of one server endpoint, as the panel holds them.
/// </summary>
public sealed record ServerConfig
{
    /// <summary>
    /// The number the panel keeps the endpoint under.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The name of the interface the endpoint runs on.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The address clients reach the endpoint at.
    /// </summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// The port the endpoint takes packets on.
    /// </summary>
    public int ListenPort { get; init; }

    /// <summary>
    /// The address ranges the interface itself carries.
    /// </summary>
    public IReadOnlyList<string> Address { get; init; } = [];

    /// <summary>
    /// The name servers given to clients.
    /// </summary>
    public IReadOnlyList<string> Dns { get; init; } = [];

    /// <summary>
    /// The address ranges routed into the tunnel on a client.
    /// </summary>
    public IReadOnlyList<string> AllowedIps { get; init; } = [];

    /// <summary>
    /// The packet size given to clients, zero when the kernel picks it.
    /// </summary>
    public int Mtu { get; init; }

    /// <summary>
    /// How often a client sends an empty packet to hold the path open, in seconds.
    /// </summary>
    public int Keepalive { get; init; }

    /// <summary>
    /// Whether the panel raises the interface of the endpoint.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Whether what clients send out is masqueraded behind the address of the host.
    /// </summary>
    public bool Nat { get; init; } = true;

    /// <summary>
    /// The ranges clients of the endpoint are not let into.
    /// </summary>
    public IReadOnlyList<string> Blocked { get; init; } = [];

    /// <summary>
    /// The private key of the interface, in base64.
    /// </summary>
    public string PrivateKey { get; init; } = string.Empty;

    /// <summary>
    /// The public key of the interface, in base64.
    /// </summary>
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>
    /// The key clients add to the handshake, in base64.
    /// </summary>
    public string PresharedKey { get; init; } = string.Empty;

    /// <summary>
    /// The obfuscation the endpoint carries.
    /// </summary>
    public ObfuscationSettings Obfuscation { get; init; } = new();

    /// <summary>
    /// When the endpoint was added.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// When the endpoint was last changed.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }
}
