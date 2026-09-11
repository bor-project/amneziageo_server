namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Traffic of a client in both directions.
/// </summary>
/// <param name="Rx">Bytes taken from the client.</param>
/// <param name="Tx">Bytes given to the client.</param>
public readonly record struct ClientUsage(ulong Rx, ulong Tx)
{
    /// <summary>
    /// Bytes in both directions together.
    /// </summary>
    public ulong Total => Rx + Tx;

    /// <summary>
    /// Returns this traffic together with another.
    /// </summary>
    public ClientUsage Add(ClientUsage other) => new(Rx + other.Rx, Tx + other.Tx);
}
