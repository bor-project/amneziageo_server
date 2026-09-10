namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// The ways a proxy takes tunnels in.
/// </summary>
public static class ProxyKind
{
    /// <summary>
    /// Takes the tunnel inside a websocket under TLS.
    /// </summary>
    public const string Ws = "ws";

    /// <summary>
    /// Takes the datagrams of the tunnel as they are and passes them on.
    /// </summary>
    public const string Wg = "wg";

    /// <summary>
    /// Every kind a proxy takes.
    /// </summary>
    public static readonly string[] All = [Ws, Wg];

    /// <summary>
    /// Tells whether a kind is one the panel knows.
    /// </summary>
    public static bool Known(string? kind) => kind is not null && Array.IndexOf(All, kind) >= 0;

    /// <summary>
    /// Tells whether a kind answers under a certificate on a path of its own.
    /// </summary>
    public static bool HasPath(string? kind) => kind == Ws;

    /// <summary>
    /// Tells whether a kind passes the datagrams on to a target of its own.
    /// </summary>
    public static bool HasTarget(string? kind) => kind == Wg;
}
