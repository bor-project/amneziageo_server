namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// The protocols a routing rule matches.
/// </summary>
public static class RouteProtocol
{
    /// <summary>
    /// Matches whatever the packet carries.
    /// </summary>
    public const string Any = "any";

    /// <summary>
    /// Matches TCP.
    /// </summary>
    public const string Tcp = "tcp";

    /// <summary>
    /// Matches UDP.
    /// </summary>
    public const string Udp = "udp";

    /// <summary>
    /// Every protocol a rule matches.
    /// </summary>
    public static readonly string[] All = [Any, Tcp, Udp];

    /// <summary>
    /// Tells whether a name is a protocol the server knows.
    /// </summary>
    public static bool Known(string? protocol) => protocol is not null && Array.IndexOf(All, protocol) >= 0;
}
