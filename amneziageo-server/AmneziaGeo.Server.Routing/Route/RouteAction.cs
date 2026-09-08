namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// What a routing rule does with the traffic it matches.
/// </summary>
public static class RouteAction
{
    /// <summary>
    /// Sends the traffic out through an outbound.
    /// </summary>
    public const string Out = "out";

    /// <summary>
    /// Drops the traffic.
    /// </summary>
    public const string Block = "block";

    /// <summary>
    /// Every action a rule takes.
    /// </summary>
    public static readonly string[] All = [Out, Block];

    /// <summary>
    /// Tells whether a name is an action the server knows.
    /// </summary>
    public static bool Known(string? action) => action is not null && Array.IndexOf(All, action) >= 0;
}
