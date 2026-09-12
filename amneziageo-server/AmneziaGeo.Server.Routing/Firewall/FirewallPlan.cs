using System.Net;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Routing.Firewall;

/// <summary>
/// A port of the host the panel asks the firewall to let in.
/// </summary>
/// <param name="Protocol">Whether the port takes tcp or udp.</param>
/// <param name="Port">The number of the port.</param>
/// <param name="Note">What the port is open for.</param>
public sealed record FirewallPort(string Protocol, int Port, string Note);

/// <summary>
/// What the panel holds open in the firewall of the host.
/// </summary>
/// <param name="Ports">The ports the host takes from outside.</param>
/// <param name="Interfaces">The interfaces the host passes packets through.</param>
public sealed record FirewallPlan(IReadOnlyList<FirewallPort> Ports, IReadOnlyList<string> Interfaces)
{
    /// <summary>
    /// The protocol a proxy and the panel take.
    /// </summary>
    public const string Tcp = "tcp";

    /// <summary>
    /// The protocol an endpoint and a relay take.
    /// </summary>
    public const string Udp = "udp";

    /// <summary>
    /// What the port of the panel is noted as.
    /// </summary>
    public const string Panel = "panel";

    /// <summary>
    /// What the port of the subscriptions is noted as.
    /// </summary>
    public const string Subscriptions = "subscriptions";

    /// <summary>
    /// A plan that holds nothing open.
    /// </summary>
    public static readonly FirewallPlan None = new([], []);

    /// <summary>
    /// Returns what the panel holds open for the endpoints, the proxies, itself and the subscriptions.
    /// </summary>
    public static FirewallPlan Of(
        IReadOnlyList<ServerConfig> configs,
        IReadOnlyList<ProxyConfig> proxies,
        PanelSettings panel,
        SubscriptionSettings subscriptions)
    {
        ArgumentNullException.ThrowIfNull(configs);
        ArgumentNullException.ThrowIfNull(proxies);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(subscriptions);

        var ports = new List<FirewallPort>();
        var interfaces = new List<string>();
        foreach (var config in configs.Where(one => one.IsEnabled && one.Opened))
        {
            Take(ports, new FirewallPort(Udp, config.ListenPort, config.Name));
            interfaces.Add(config.Name);
        }

        foreach (var proxy in proxies.Where(one => one.IsEnabled && one.Opened))
        {
            Take(ports, new FirewallPort(ProxyKind.HasTarget(proxy.Kind) ? Udp : Tcp, proxy.Port, proxy.Name));
        }

        if (panel.Opened && Reached(panel.Listen))
        {
            Take(ports, new FirewallPort(Tcp, panel.Port, Panel));
        }

        if (subscriptions.IsEnabled && subscriptions.Opened && Reached(subscriptions.Listen))
        {
            Take(ports, new FirewallPort(Tcp, subscriptions.Port, Subscriptions));
        }

        return new FirewallPlan(ports, interfaces);
    }

    // Keeps a port once, under the first thing that asked for it.
    private static void Take(List<FirewallPort> ports, FirewallPort port)
    {
        if (port.Port is <= 0 or > 65535)
        {
            return;
        }

        if (!ports.Exists(held => held.Port == port.Port
            && string.Equals(held.Protocol, port.Protocol, StringComparison.Ordinal)))
        {
            ports.Add(port);
        }
    }

    // Tells whether what is bound is reached from outside the host.
    private static bool Reached(IReadOnlyList<string> listen) => listen.Count == 0
        || listen.Any(address => !IPAddress.TryParse(address, out var found) || !IPAddress.IsLoopback(found));
}
