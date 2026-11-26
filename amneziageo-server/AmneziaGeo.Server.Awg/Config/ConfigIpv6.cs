using System.Net;
using System.Security.Cryptography;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Awg.Config;

public sealed record ConfigIpv6Plan(IReadOnlyList<string> Address, string AutomaticAddress, bool HasSpace = true);

public static class ConfigIpv6
{
    /// <summary>
    /// Finds the retained automatic address.
    /// </summary>
    public static string Automatic(IReadOnlyList<string> addresses, string? automatic) =>
        AwgAllowedIp.TryParse(automatic, out var range) && range.IsSix
            ? addresses.FirstOrDefault(one => Same(one, automatic!)) ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Updates the automatic IPv6 address of a draft.
    /// </summary>
    public static ConfigIpv6Plan Refresh(IReadOnlyList<string> addresses, string automatic, bool available,
        IEnumerable<string> reserved, Func<string>? candidate = null)
    {
        var marker = Automatic(addresses, automatic);
        var blocked = reserved.Select(Parse).OfType<AwgAllowedIp>().Where(one => one.IsSix).ToArray();
        if (!available)
        {
            return marker.Length == 0
                ? new(addresses, string.Empty)
                : new(addresses.Where(one => !Same(one, marker)).ToArray(), string.Empty);
        }
        if (marker.Length > 0 && !blocked.Any(one => Overlap(one, AwgAllowedIp.Parse(marker))))
        {
            return new(addresses, marker);
        }
        var kept = marker.Length == 0 ? addresses : addresses.Where(one => !Same(one, marker)).ToArray();
        if (kept.Any(one => Parse(one) is { IsSix: true }))
        {
            return new(kept, string.Empty);
        }
        for (var at = 0; at < 256; at++)
        {
            var text = candidate?.Invoke() ?? Fresh();
            var range = AwgAllowedIp.Parse(text);
            if (range.IsSix && range.Cidr == 64 && !blocked.Any(one => Overlap(one, range)))
            {
                return new([.. kept, text], text);
            }
        }
        return new(addresses, marker, false);
    }

    /// <summary>
    /// Identifies intersecting address ranges.
    /// </summary>
    public static bool Overlap(AwgAllowedIp left, AwgAllowedIp right) =>
        left.IsSix == right.IsSix && (ClientPool.Inside(left, right.Address) || ClientPool.Inside(right, left.Address));

    private static bool Same(string left, string right) => Parse(left) is { } first && Parse(right) is { } second
        && first.Cidr == second.Cidr && first.Address.Equals(second.Address);

    private static AwgAllowedIp? Parse(string? value) => AwgAllowedIp.TryParse(value, out var range) ? range : null;

    private static string Fresh()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes.AsSpan(1, 5));
        bytes[0] = 0xfd;
        bytes[15] = 1;
        return new IPAddress(bytes) + "/64";
    }
}
