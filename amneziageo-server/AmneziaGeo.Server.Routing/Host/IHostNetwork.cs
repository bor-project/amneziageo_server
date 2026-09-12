namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Puts interfaces, addresses, routes and firewall rules on the host.
/// </summary>
public interface IHostNetwork
{
    /// <summary>
    /// Tells whether the host carries an interface.
    /// </summary>
    bool HasLink(string name);

    /// <summary>
    /// Adds an AmneziaWG interface.
    /// </summary>
    Task AddLinkAsync(string name, CancellationToken ct);

    /// <summary>
    /// Takes an interface off the host.
    /// </summary>
    Task RemoveLinkAsync(string name, CancellationToken ct);

    /// <summary>
    /// Replaces the address ranges of an interface.
    /// </summary>
    Task AddressAsync(string name, IReadOnlyList<string> addresses, CancellationToken ct);

    /// <summary>
    /// Sets the packet size of an interface and brings it up.
    /// </summary>
    Task UpAsync(string name, int mtu, CancellationToken ct);

    /// <summary>
    /// Puts the way out through an interface into a routing table.
    /// </summary>
    Task RouteAsync(string name, int table, CancellationToken ct);

    /// <summary>
    /// Clears a routing table.
    /// </summary>
    Task ClearRouteAsync(int table, CancellationToken ct);

    /// <summary>
    /// Adds or removes the rule that sends a marked packet into a routing table.
    /// </summary>
    Task RuleAsync(uint mark, int table, int priority, bool present, CancellationToken ct);

    /// <summary>
    /// Returns the interface the host reaches the internet through.
    /// </summary>
    Task<string> UplinkAsync(CancellationToken ct);

    /// <summary>
    /// Lets the host pass packets between interfaces, in both families.
    /// </summary>
    Task ForwardingAsync(CancellationToken ct);

    /// <summary>
    /// Puts a firewall ruleset on the host, replacing the one it holds.
    /// </summary>
    Task FirewallAsync(string ruleset, CancellationToken ct);

    /// <summary>
    /// Reads a firewall ruleset without putting it on the host.
    /// </summary>
    Task CheckFirewallAsync(string ruleset, CancellationToken ct);

    /// <summary>
    /// Returns an inet table of the firewall in the JSON of nft, empty when the host holds no such table.
    /// </summary>
    Task<string> ReadFirewallAsync(string table, CancellationToken ct);
}
