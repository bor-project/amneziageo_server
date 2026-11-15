using System.Globalization;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Writes a standard WireGuard link.
/// </summary>
public static class ClientWireLink
{
    /// <summary>
    /// Returns the link of a compatible client.
    /// </summary>
    public static string Link(ServerConfig config, TunnelClient client, ClientTemplate? template = null, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(client);

        if (!ConfigRules.IsPlain(config.Obfuscation)
            || string.IsNullOrEmpty(config.Host)
            || string.IsNullOrEmpty(config.PublicKey)
            || string.IsNullOrEmpty(client.PrivateKey)
            || client.Address.Count == 0
            || ClientText.Preshared(config, client).Length > 0)
        {
            return string.Empty;
        }

        var mtu = template?.Mtu ?? config.Mtu;
        var link = $"wireguard://{Uri.EscapeDataString(client.PrivateKey)}@{ClientText.Endpoint(config)}"
            + $"?publickey={Uri.EscapeDataString(config.PublicKey)}&address={Uri.EscapeDataString(string.Join(",", client.Address))}";
        if (mtu > 0)
        {
            link += $"&mtu={mtu.ToString(CultureInfo.InvariantCulture)}";
        }

        var name = title ?? ClientText.Title(config, client);

        return link + "&reserved=0,0,0" + (name.Length > 0 ? "#" + Uri.EscapeDataString(name) : string.Empty);
    }
}
