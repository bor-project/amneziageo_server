using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Puts the routing rules the panel holds on the host.
/// </summary>
public sealed class RouteHost
{
    private readonly IHostNetwork _network;

    /// <summary>
    /// ctor
    /// </summary>
    public RouteHost(IHostNetwork network) => _network = network;

    /// <summary>
    /// Puts the rules on the host, replacing the ones it carries.
    /// </summary>
    public Task ApplyAsync(RoutePlan plan, CancellationToken ct) =>
        _network.FirewallAsync(RouteRuleset.Text(plan), ct);

    /// <summary>
    /// Reads the rules without putting them on the host.
    /// </summary>
    public Task CheckAsync(RoutePlan plan, CancellationToken ct) =>
        _network.CheckFirewallAsync(RouteRuleset.Text(plan), ct);
}
