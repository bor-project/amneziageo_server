using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// The values a client file takes where its template names none.
/// </summary>
public static class TemplateDefaults
{
    /// <summary>
    /// The name the built in template takes.
    /// </summary>
    public const string Name = "default";

    /// <summary>
    /// The range that carries the whole of IPv4.
    /// </summary>
    public const string AnyFour = "0.0.0.0/0";

    /// <summary>
    /// The range that carries the whole of IPv6.
    /// </summary>
    public const string AnySix = "::/0";

    /// <summary>
    /// The packet size.
    /// </summary>
    public const int Mtu = ConfigDefaults.Mtu;

    /// <summary>
    /// How often the client holds the path open, in seconds.
    /// </summary>
    public const int Keepalive = ConfigDefaults.Keepalive;

    /// <summary>
    /// Whether the application of the client routes on its own.
    /// </summary>
    public const bool Routing = true;

    /// <summary>
    /// The name servers.
    /// </summary>
    public static IReadOnlyList<string> Dns => ConfigDefaults.Dns;

    /// <summary>
    /// Returns the template a fresh database starts with.
    /// </summary>
    public static ClientTemplate Fresh() => new()
    {
        Name = Name,
        AllowedIps = [AnyFour, AnySix],
        Dns = [.. Dns],
        Mtu = Mtu,
        Keepalive = Keepalive,
    };

    /// <summary>
    /// Returns the ranges a holder of the given addresses routes into the tunnel.
    /// </summary>
    public static IReadOnlyList<string> AllowedIps(IEnumerable<string> address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.Any(IsSix) ? [AnyFour, AnySix] : [AnyFour];
    }

    private static bool IsSix(string range) =>
        IPAddress.TryParse(range.Split('/')[0], out var address) && address.AddressFamily == AddressFamily.InterNetworkV6;
}
