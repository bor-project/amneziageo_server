using System.Net;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Turns the name of a server into an address.
/// </summary>
public static class HostAddress
{
    /// <summary>
    /// Returns the address a name stands for, taking IPv4 first.
    /// </summary>
    public static async Task<IPAddress> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        var found = await System.Net.Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

        return found.FirstOrDefault(one => one.AddressFamily == AddressFamily.InterNetwork)
            ?? found.FirstOrDefault()
            ?? throw new HostNetworkException($"'{host}' does not resolve to an address");
    }
}
