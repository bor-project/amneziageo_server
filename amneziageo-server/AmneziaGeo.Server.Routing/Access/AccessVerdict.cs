namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// How a connection of a client was carried.
/// </summary>
public static class AccessVerdict
{
    /// <summary>
    /// Sent out through an outbound.
    /// </summary>
    public const string Out = "out";

    /// <summary>
    /// Let out the way the host sends its own.
    /// </summary>
    public const string Host = "host";

    /// <summary>
    /// Dropped by a blocking rule or the block list.
    /// </summary>
    public const string Block = "block";

    /// <summary>
    /// Dropped while the channel of its rule carries nothing.
    /// </summary>
    public const string Held = "held";

    /// <summary>
    /// Dropped by a guard of the resolver.
    /// </summary>
    public const string Guard = "guard";

    /// <summary>
    /// Every verdict a record can carry.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Out, Host, Block, Held, Guard];
}
