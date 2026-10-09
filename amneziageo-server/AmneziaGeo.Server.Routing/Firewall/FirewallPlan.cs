using System.Net;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Routing.Firewall;

/// <summary>
/// A port of the host the panel asks the firewall to let in.
/// </summary>
/// <param name="Protocol">Whether the port takes tcp or udp.</param>
/// <param name="Port">The number of the port.</param>
/// <param name="Note">What the port is open for.</param>
/// <param name="Interface">The interface the port is open on, empty for every one.</param>
public sealed record FirewallPort(string Protocol, int Port, string Note, string Interface = "");

/// <summary>
/// What the panel holds open in the firewall of the host.
/// </summary>
/// <param name="Ports">The ports the host takes from outside.</param>
/// <param name="Interfaces">The interfaces the host passes packets through.</param>
public sealed record FirewallPlan(IReadOnlyList<FirewallPort> Ports, IReadOnlyList<string> Interfaces)
{
    /// <summary>
    /// The protocol the services of an endpoint and the panel take.
    /// </summary>
    public const string Tcp = "tcp";

    /// <summary>
    /// The protocol an endpoint takes.
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
    /// Returns what the panel holds open for the endpoints with their services and the resolver their clients ask,
    /// itself and the subscriptions.
    /// </summary>
    public static FirewallPlan Of(
        IReadOnlyList<ServerConfig> configs,
        PanelSettings panel,
        SubscriptionSettings subscriptions,
        DnsSettings? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(configs);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(subscriptions);

        var ports = new List<FirewallPort>();
        var interfaces = new List<string>();
        foreach (var config in configs.Where(one => one.IsEnabled && one.Opened))
        {
            Take(ports, new FirewallPort(Udp, config.ListenPort, config.Name));
            Take(ports, new FirewallPort(Tcp, ConfigServices.Port(config), config.Name));
            if (resolver is not null && Answers(resolver, config))
            {
                Take(ports, new FirewallPort(Udp, resolver.Port, config.Name, config.Name));
                Take(ports, new FirewallPort(Tcp, resolver.Port, config.Name, config.Name));
            }

            interfaces.Add(config.Name);
        }

        if (panel.Opened && Reached(panel.Listen))
        {
            Take(ports, new FirewallPort(Tcp, panel.Port, Panel));
        }

        if (subscriptions.IsEnabled && subscriptions.Separate && subscriptions.Opened && Reached(subscriptions.Listen))
        {
            Take(ports, new FirewallPort(Tcp, subscriptions.Port, Subscriptions));
        }

        return new FirewallPlan(ports, interfaces);
    }

    /// <summary>
    /// Returns the ports the plan before a change held open and the plan after it holds no longer, whatever held them;
    /// a port that only passed to another owner is still held.
    /// </summary>
    public static IReadOnlyList<FirewallPort> Dropped(FirewallPlan before, FirewallPlan after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        return [.. before.Ports.Where(port => !after.Ports.Any(held => Same(held, port)))];
    }

    // Keeps a port once, under the first thing that asked for it.
    private static void Take(List<FirewallPort> ports, FirewallPort port)
    {
        if (port.Port is <= 0 or > 65535)
        {
            return;
        }

        if (!ports.Exists(held => Same(held, port)))
        {
            ports.Add(port);
        }
    }

    // Tells whether two ports are the same port of the host: the protocol, the number and the interface, whatever asked.
    private static bool Same(FirewallPort one, FirewallPort other) =>
        one.Port == other.Port
        && string.Equals(one.Protocol, other.Protocol, StringComparison.Ordinal)
        && string.Equals(one.Interface, other.Interface, StringComparison.Ordinal);

    // Tells whether the resolver answers on an address of an endpoint.
    private static bool Answers(DnsSettings resolver, ServerConfig config)
    {
        if (!resolver.IsEnabled)
        {
            return false;
        }

        if (resolver.Listen.Count == 0)
        {
            return true;
        }

        var own = config.Address.Select(Bare).OfType<IPAddress>().ToArray();

        return resolver.Listen.Any(one => DnsRules.Address(one, out var address) && own.Contains(address));
    }

    // Returns the address a range is written with.
    private static IPAddress? Bare(string range)
    {
        var slash = range.IndexOf('/', StringComparison.Ordinal);

        return IPAddress.TryParse((slash < 0 ? range : range[..slash]).Trim(), out var address) ? address : null;
    }

    /// <summary>
    /// Tells whether what is bound is reached from outside the host.
    /// </summary>
    public static bool Reached(IReadOnlyList<string> listen) => listen.Count == 0
        || listen.Any(address => !IPAddress.TryParse(address, out var found) || !IPAddress.IsLoopback(found));
}
