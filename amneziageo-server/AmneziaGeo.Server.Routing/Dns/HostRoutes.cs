using System.Globalization;
using System.Net;
using System.Numerics;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// A route of the host.
/// </summary>
/// <param name="Network">The network the route leads to.</param>
/// <param name="PrefixLength">The length of the prefix of the network.</param>
/// <param name="IsReject">Whether the route turns the traffic away.</param>
public sealed record HostRoute(IPAddress Network, int PrefixLength, bool IsReject)
{
    /// <summary>
    /// The network with its prefix.
    /// </summary>
    public string Range => $"{Network}/{PrefixLength.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Reads the routes of the host and tells a network behind it from one it reaches by its default route.
/// </summary>
public static class HostRoutes
{
    private const uint FlagUp = 0x0001;

    private const uint FlagReject = 0x0200;

    private static readonly char[] Breaks = ['\t', ' '];

    /// <summary>
    /// Returns the routes of the host, none where the host shows none.
    /// </summary>
    public static IReadOnlyList<HostRoute> Read() =>
        [.. Four(Text("/proc/net/route")), .. Six(Text("/proc/net/ipv6_route"))];

    /// <summary>
    /// Tells whether the host reaches an address by a route of its own rather than by its default route.
    /// </summary>
    public static bool Behind(IPAddress address, IReadOnlyList<HostRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(routes);

        var best = routes
            .Where(route => route.Network.AddressFamily == address.AddressFamily && RouteProbe.Within([route.Range], address))
            .MaxBy(route => route.PrefixLength);

        return best is { PrefixLength: > 0, IsReject: false };
    }

    /// <summary>
    /// Reads the IPv4 routes that are up out of the text of /proc/net/route.
    /// </summary>
    public static IReadOnlyList<HostRoute> Four(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var routes = new List<HostRoute>();
        foreach (var line in text.Split('\n').Skip(1))
        {
            var parts = line.Split(Breaks, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8
                || !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var network)
                || !uint.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags)
                || !uint.TryParse(parts[7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask)
                || (flags & FlagUp) == 0)
            {
                continue;
            }

            routes.Add(new HostRoute(new IPAddress(BitConverter.GetBytes(network)), BitOperations.PopCount(mask), (flags & FlagReject) != 0));
        }

        return routes;
    }

    /// <summary>
    /// Reads the IPv6 routes that are up out of the text of /proc/net/ipv6_route.
    /// </summary>
    public static IReadOnlyList<HostRoute> Six(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var routes = new List<HostRoute>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split(Breaks, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9
                || parts[0].Length != 32
                || !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var prefix)
                || !uint.TryParse(parts[8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags)
                || (flags & FlagUp) == 0
                || prefix is < 0 or > 128)
            {
                continue;
            }

            var bytes = Bytes(parts[0]);
            if (bytes is null)
            {
                continue;
            }

            routes.Add(new HostRoute(new IPAddress(bytes), prefix, (flags & FlagReject) != 0));
        }

        return routes;
    }

    private static byte[]? Bytes(string hex)
    {
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Text(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
