namespace AmneziaGeo.Server.Awg.Device;

/// <summary>
/// One AmneziaWG interface as the kernel holds it.
/// </summary>
public sealed record AwgDevice
{
    /// <summary>
    /// The name of the interface.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The index the kernel gave the interface.
    /// </summary>
    public uint Index { get; init; }

    /// <summary>
    /// The port the interface takes packets on.
    /// </summary>
    public ushort ListenPort { get; init; }

    /// <summary>
    /// The mark put on outgoing packets, zero when there is none.
    /// </summary>
    public uint Fwmark { get; init; }

    /// <summary>
    /// The private key of the interface, in base64.
    /// </summary>
    public string? PrivateKey { get; init; }

    /// <summary>
    /// The public key of the interface, in base64.
    /// </summary>
    public string? PublicKey { get; init; }

    /// <summary>
    /// The obfuscation the interface carries.
    /// </summary>
    public AwgObfuscation Obfuscation { get; init; } = new();

    /// <summary>
    /// The peers of the interface.
    /// </summary>
    public IReadOnlyList<AwgPeer> Peers { get; init; } = [];
}
