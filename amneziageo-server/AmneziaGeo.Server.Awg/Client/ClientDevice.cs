using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Turns the clients of an endpoint into a change for the kernel.
/// </summary>
public static class ClientDevice
{
    /// <summary>
    /// Returns the change that puts the clients of an endpoint on its interface.
    /// </summary>
    public static AwgUpdate Update(ServerConfig config, IReadOnlyList<TunnelClient> clients)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(clients);

        return new AwgUpdate
        {
            Name = config.Name,
            Peers = [.. clients.Where(client => client.IsEnabled).Select(client => Peer(config, client))],
        };
    }

    /// <summary>
    /// Returns the change that takes clients off the interface of an endpoint.
    /// </summary>
    public static AwgUpdate Away(ServerConfig config, IEnumerable<string> publicKeys)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(publicKeys);

        return new AwgUpdate
        {
            Name = config.Name,
            Peers = [.. publicKeys.Select(key => new AwgPeerUpdate { PublicKey = key, Remove = true })],
        };
    }

    /// <summary>
    /// Returns one client as a peer of the interface.
    /// </summary>
    public static AwgPeerUpdate Peer(ServerConfig config, TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(client);

        var preshared = ClientText.Preshared(config, client);

        return new AwgPeerUpdate
        {
            PublicKey = client.PublicKey,
            PresharedKey = preshared.Length > 0 ? preshared : null,
            ReplaceAllowedIps = true,
            AllowedIps = [.. client.Address.Select(Range).OfType<AwgAllowedIp>()],
        };
    }

    /// <summary>
    /// Returns the peers an interface carries that the panel does not hold as clients of it.
    /// </summary>
    public static IReadOnlyList<string> Stale(IEnumerable<string> present, IReadOnlyList<TunnelClient> clients)
    {
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(clients);

        var held = new HashSet<string>(
            clients.Where(client => client.IsEnabled).Select(client => client.PublicKey),
            StringComparer.Ordinal);

        return [.. present.Where(key => !held.Contains(key))];
    }

    private static AwgAllowedIp? Range(string text) => AwgAllowedIp.TryParse(text, out var range) ? range : null;
}
