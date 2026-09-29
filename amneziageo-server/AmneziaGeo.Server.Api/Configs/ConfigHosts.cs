using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// Finds the host the clients of a new endpoint reach the server at.
/// </summary>
public static class ConfigHosts
{
    private static readonly string[] Local = [".local", ".localhost", ".localdomain", ".lan", ".home", ".internal", ".home.arpa"];

    /// <summary>
    /// Returns the host the clients of a new endpoint reach the server at, empty when the server knows none.
    /// </summary>
    public static string Guess(
        IReadOnlyList<ServerConfig> configs,
        PanelSettings panel,
        string root,
        string asked,
        IReadOnlyList<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(configs);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(addresses);

        return configs.OrderBy(one => one.Id).Select(one => one.Host.Trim()).FirstOrDefault(one => one.Length > 0)
            ?? Certified(panel, root)
            ?? panel.Domains.FirstOrDefault(Reached)
            ?? (Reached(asked) ? asked : null)
            ?? addresses.FirstOrDefault(one => IPAddress.TryParse(one, out var address)
                && address.AddressFamily == AddressFamily.InterNetwork
                && Public(address))
            ?? string.Empty;
    }

    // Tells whether clients outside the host reach it by a name or an address.
    private static bool Reached(string host)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return Public(address);
        }

        var name = host.Trim().TrimEnd('.').ToLowerInvariant();

        return name.Contains('.', StringComparison.Ordinal)
            && !Local.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal))
            && ConfigRules.CheckHost(name) is null;
    }

    // Tells whether an address is reached from the internet.
    private static bool Public(IPAddress address)
    {
        var plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        var bytes = plain.GetAddressBytes();
        if (plain.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xE0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8);
        }

        return bytes[0] switch
        {
            0 or 10 or 127 => false,
            >= 224 => false,
            100 => bytes[1] is < 64 or > 127,
            169 => bytes[1] != 254,
            172 => bytes[1] is < 16 or > 31,
            192 => !(bytes[1] == 168 || (bytes[1] == 0 && bytes[2] is 0 or 2)),
            198 => bytes[1] is not (18 or 19) && !(bytes[1] == 51 && bytes[2] == 100),
            203 => !(bytes[1] == 0 && bytes[2] == 113),
            _ => true,
        };
    }

    // Returns the domain the certificate of the panel was issued for, when it lies under the root of the certificates.
    private static string? Certified(PanelSettings panel, string root)
    {
        if (panel.Certificate.Length == 0 || root.Length == 0)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(panel.Certificate) ?? string.Empty;
        var name = Path.GetFileName(folder);

        return string.Equals(Path.GetDirectoryName(folder), root.TrimEnd('/'), StringComparison.Ordinal) && Reached(name)
            ? name
            : null;
    }
}
