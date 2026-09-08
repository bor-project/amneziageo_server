namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// What a rule carries before anyone fills it in.
/// </summary>
public static class RouteDefaults
{
    /// <summary>
    /// Returns an empty rule under a name.
    /// </summary>
    public static RouteRule Fresh(string name) => new()
    {
        Name = name,
        Action = RouteAction.Out,
        Protocol = RouteProtocol.Any,
        IsEnabled = true,
    };
}
