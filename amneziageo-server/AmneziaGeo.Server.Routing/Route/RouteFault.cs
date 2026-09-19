namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// Why a routing rule is unusable.
/// </summary>
/// <param name="Code">The name of the refusal the panel translates.</param>
/// <param name="Message">The reason in plain words.</param>
public sealed record RouteFault(string Code, string Message)
{
    private static readonly string[] Ways = ["unknown-outbound", "outbound-off", "balancer-off", "no-live-member"];

    /// <summary>
    /// Tells whether the rule itself is good and only the way out of it carries nothing.
    /// </summary>
    public bool IsWayGone => Array.IndexOf(Ways, Code) >= 0;
}
