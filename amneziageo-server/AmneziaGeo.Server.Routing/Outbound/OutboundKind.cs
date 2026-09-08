namespace AmneziaGeo.Server.Routing.Outbound;

/// <summary>
/// The ways traffic leaves the host.
/// </summary>
public static class OutboundKind
{
    /// <summary>
    /// Leaves through the uplink of the host itself.
    /// </summary>
    public const string Local = "local";

    /// <summary>
    /// Leaves through an AmneziaWG tunnel the host holds as a client.
    /// </summary>
    public const string Wg = "wg";

    /// <summary>
    /// Every kind an outbound takes.
    /// </summary>
    public static readonly string[] All = [Local, Wg];

    /// <summary>
    /// Tells whether a kind is one the server knows.
    /// </summary>
    public static bool Known(string? kind) => kind is not null && Array.IndexOf(All, kind) >= 0;

    /// <summary>
    /// Tells whether a kind carries an interface of its own.
    /// </summary>
    public static bool HasLink(string? kind) => kind == Wg;
}
