using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Routing.Firewall;

namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// Picks the port of the host the connections handed over come in through where its firewall drops what comes in
/// for another address.
/// </summary>
public static class NameDoor
{
    /// <summary>
    /// Returns the TCP ports the panel holds on every interface, its own first.
    /// </summary>
    public static IReadOnlyList<int> Ports(FirewallPlan plan, PanelSettings panel)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(panel);

        var ports = new List<int>();
        if (FirewallPlan.Reached(panel.Listen) && panel.Port is > 0 and <= 65535)
        {
            ports.Add(panel.Port);
        }

        foreach (var port in plan.Ports)
        {
            if (port.Protocol == FirewallPlan.Tcp && port.Interface.Length == 0 && !ports.Contains(port.Port))
            {
                ports.Add(port.Port);
            }
        }

        return ports;
    }

    /// <summary>
    /// Returns the ports the firewall of the host lets in, then the ones the panel cannot tell about.
    /// </summary>
    public static IReadOnlyList<int> Pick(IReadOnlyList<int> ports, IReadOnlyList<string> states)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(states);

        var open = ports.Where((_, at) => at < states.Count && states[at] == PortState.Open);
        var unknown = ports.Where((_, at) => at >= states.Count || states[at] == PortState.Unknown);

        return [.. open, .. unknown];
    }
}
