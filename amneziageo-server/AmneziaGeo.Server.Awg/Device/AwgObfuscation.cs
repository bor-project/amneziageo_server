namespace AmneziaGeo.Server.Awg.Device;

/// <summary>
/// The obfuscation an interface carries on top of the WireGuard protocol.
/// </summary>
public sealed record AwgObfuscation
{
    /// <summary>
    /// How many junk packets go before a handshake.
    /// </summary>
    public ushort Jc { get; init; }

    /// <summary>
    /// The shortest junk packet, in bytes.
    /// </summary>
    public ushort Jmin { get; init; }

    /// <summary>
    /// The longest junk packet, in bytes.
    /// </summary>
    public ushort Jmax { get; init; }

    /// <summary>
    /// Junk prepended to a handshake initiation, in bytes.
    /// </summary>
    public ushort S1 { get; init; }

    /// <summary>
    /// Junk prepended to a handshake response, in bytes.
    /// </summary>
    public ushort S2 { get; init; }

    /// <summary>
    /// Junk prepended to a cookie reply, in bytes.
    /// </summary>
    public ushort S3 { get; init; }

    /// <summary>
    /// Junk prepended to a transport packet, in bytes.
    /// </summary>
    public ushort S4 { get; init; }

    /// <summary>
    /// The type written into a handshake initiation.
    /// </summary>
    public AwgRange H1 { get; init; }

    /// <summary>
    /// The type written into a handshake response.
    /// </summary>
    public AwgRange H2 { get; init; }

    /// <summary>
    /// The type written into a cookie reply.
    /// </summary>
    public AwgRange H3 { get; init; }

    /// <summary>
    /// The type written into a transport packet.
    /// </summary>
    public AwgRange H4 { get; init; }

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
    public string? HeaderProtectionKey { get; init; }

    /// <summary>
    /// Padding added to the content of a transport packet, in bytes.
    /// </summary>
    public AwgRange ContentPaddingAddition { get; init; }

    /// <summary>
    /// When a session is renewed, in seconds.
    /// </summary>
    public AwgRange RekeyAfterTime { get; init; }

    /// <summary>
    /// How long a handshake attempt waits before it repeats, in seconds.
    /// </summary>
    public AwgRange RekeyTimeout { get; init; }

    /// <summary>
    /// When a session stops being accepted, in seconds.
    /// </summary>
    public AwgRange RejectAfterTime { get; init; }

    /// <summary>
    /// How long a quiet path is held open, in seconds.
    /// </summary>
    public AwgRange KeepaliveTimeout { get; init; }

    /// <summary>
    /// How many handshakes are attempted before the peer is given up on.
    /// </summary>
    public AwgRange MaxHandshakeAttempts { get; init; }

    /// <summary>
    /// Whether random bytes are appended to packets.
    /// </summary>
    public bool RandomTrailers { get; init; }

    /// <summary>
    /// Whether cookie replies are turned off.
    /// </summary>
    public bool DisableCookies { get; init; }
}
