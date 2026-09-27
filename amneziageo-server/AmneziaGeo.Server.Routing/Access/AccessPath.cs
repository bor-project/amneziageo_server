namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// Whether a connection that went out left the host itself or was handed to another server.
/// </summary>
public static class AccessPath
{
    /// <summary>
    /// Left through the uplink of the host.
    /// </summary>
    public const string Local = "local";

    /// <summary>
    /// Handed to another server through a tunnel.
    /// </summary>
    public const string Relay = "relay";

    /// <summary>
    /// Every path a record can carry.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Local, Relay];
}
