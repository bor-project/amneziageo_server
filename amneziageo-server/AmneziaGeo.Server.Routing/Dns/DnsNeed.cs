using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Tells when the routing needs the resolver.
/// </summary>
public static class DnsNeed
{
    /// <summary>
    /// Tells whether a rule or a basic list on the host matches by name or a template answers with stand-in addresses.
    /// </summary>
    public static bool Of(RoutePlan? plan, bool standIns) => standIns || plan is { HasNames: true };

    /// <summary>
    /// Tells whether the resolver is kept: the routing needs it, clients pick it or clients hold files that name it.
    /// </summary>
    public static bool Kept(bool needed, int holders, int picked) => needed || holders > 0 || picked > 0;

    /// <summary>
    /// Returns the settings the resolver runs with: switched on while the panel allows it and the routing needs it.
    /// </summary>
    public static DnsSettings Running(DnsSettings chosen, bool needed)
    {
        ArgumentNullException.ThrowIfNull(chosen);

        return chosen with { IsEnabled = chosen.IsEnabled && needed };
    }
}
