namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// What a rule carries before anyone fills it in.
/// </summary>
public static class RouteDefaults
{
    public const string FallbackKey = "route-fallback";

    public const string InternetOnly = "internet-only";

    public static IReadOnlyList<string> PrivateNetworks { get; } =
    [
        .. Host.OutboundRuleset.Private4, "127.0.0.0/8",
        .. Host.OutboundRuleset.Private6, "::1/128",
    ];
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
