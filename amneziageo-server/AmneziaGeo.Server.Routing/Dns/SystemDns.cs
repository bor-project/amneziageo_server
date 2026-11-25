using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Reads system name servers.
/// </summary>
public static class SystemDns
{
    public static IReadOnlyList<string> Servers(
        IEnumerable<string>? excludedAddresses = null,
        int resolverPort = DnsDefaults.Port,
        ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        var candidates = new List<IPAddress>();
        var local = new List<IPAddress>();
        var excluded = (excludedAddresses ?? []).ToList();
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces().Where(one => one.OperationalStatus == OperationalStatus.Up))
            {
                try
                {
                    var properties = network.GetIPProperties();
                    candidates.AddRange(properties.DnsAddresses);
                    local.AddRange(properties.UnicastAddresses.Select(one => one.Address));
                }
                catch (NetworkInformationException ex)
                {
                    log.LogWarning(ex, "the DNS addresses of interface {Interface} were not read", network.Name);
                }
            }
        }
        catch (NetworkInformationException ex)
        {
            log.LogWarning(ex, "the network interfaces were not read");

            return [];
        }

        var wildcard4 = excluded.Any(one => Address(one)?.Equals(IPAddress.Any) == true);
        var wildcard6 = excluded.Any(one => Address(one)?.Equals(IPAddress.IPv6Any) == true);
        if (resolverPort == DnsDefaults.Port)
        {
            excluded.AddRange(local.Where(one => one.AddressFamily == AddressFamily.InterNetwork ? wildcard4 : wildcard6).Select(one => one.ToString()));
        }

        return Filter(candidates, excluded, resolverPort);
    }

    internal static IReadOnlyList<string> Filter(IEnumerable<IPAddress> servers, IEnumerable<string> excludedAddresses, int resolverPort)
    {
        var excluded = excludedAddresses.Select(Address).OfType<IPAddress>().Select(Normalize).ToHashSet();
        var wildcard4 = excluded.Contains(IPAddress.Any);
        var wildcard6 = excluded.Contains(IPAddress.IPv6Any);

        return
        [
            .. servers.Select(Normalize)
                .Where(one => !one.Equals(IPAddress.Any) && !one.Equals(IPAddress.IPv6Any) && !one.Equals(IPAddress.None) && !one.IsIPv6Multicast)
                .Where(one => resolverPort != DnsDefaults.Port || !excluded.Contains(one)
                    && !(IPAddress.IsLoopback(one) && (one.AddressFamily == AddressFamily.InterNetwork ? wildcard4 : wildcard6)))
                .Distinct().Select(one => one.ToString())
        ];
    }

    private static IPAddress? Address(string text)
    {
        var slash = text.IndexOf('/', StringComparison.Ordinal);

        return IPAddress.TryParse((slash < 0 ? text : text[..slash]).Trim(), out var address) ? address : null;
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
