using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Hands out the addresses clients carry inside the ranges of an endpoint.
/// </summary>
public static class ClientPool
{
    /// <summary>
    /// The most addresses looked through in one range.
    /// </summary>
    public const ulong MaxSearch = 65536;

    /// <summary>
    /// Returns the addresses of the first number free in every range of an endpoint, one out of each range.
    /// </summary>
    public static IReadOnlyList<string> Free(IReadOnlyList<string> ranges, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(taken);

        var own = Ranges(ranges);
        if (own.Count == 0)
        {
            return [];
        }

        var held = Held(taken);
        var room = own.Min(Room);
        for (var number = 1UL; number < room; number++)
        {
            var at = (UInt128)number;
            var picked = own.Select(range => Nth(range, at)).ToArray();
            if (own.TrueForAll(range => !Reserved(range, at)) && !picked.Any(held.Contains))
            {
                return [.. picked.Select(address => $"{address}/{Width(address)}")];
            }
        }

        return [];
    }

    /// <summary>
    /// Returns why the addresses of a client do not fit the ranges of its endpoint, or null when they fit.
    /// </summary>
    public static ClientFault? Fit(IReadOnlyList<string> ranges, IReadOnlyList<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(addresses);

        var own = Ranges(ranges);
        var used = new HashSet<AwgAllowedIp>();
        foreach (var text in addresses)
        {
            if (!AwgAllowedIp.TryParse(text, out var address) || address.Cidr != Width(address.Address))
            {
                return new ClientFault("bad-client-address", $"'{text}' is not a single address");
            }

            var range = own.Find(one => Inside(one, address.Address));
            if (range is null)
            {
                return new ClientFault(
                    "client-address-outside",
                    $"{address.Address} lies outside the ranges of the endpoint");
            }

            if (Reserved(range, Number(range, address.Address)))
            {
                return new ClientFault(
                    "client-address-reserved",
                    $"{address.Address} is the network, the broadcast or the address of the endpoint");
            }

            if (!used.Add(range))
            {
                return new ClientFault("bad-client-address", $"the client carries two addresses in {range}");
            }
        }

        return null;
    }

    /// <summary>
    /// Tells whether an address falls inside a range.
    /// </summary>
    public static bool Inside(AwgAllowedIp range, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(address);

        if (range.Address.AddressFamily != address.AddressFamily)
        {
            return false;
        }

        var bounds = range.Address.GetAddressBytes();
        var bytes = address.GetAddressBytes();
        Mask(bounds, range.Cidr);
        Mask(bytes, range.Cidr);

        return bounds.AsSpan().SequenceEqual(bytes);
    }

    private static List<AwgAllowedIp> Ranges(IEnumerable<string> ranges)
    {
        var own = new List<AwgAllowedIp>();
        foreach (var text in ranges)
        {
            if (AwgAllowedIp.TryParse(text, out var range))
            {
                own.Add(range);
            }
        }

        return own;
    }

    private static bool Reserved(AwgAllowedIp range, UInt128 number)
    {
        if (number == Number(range, range.Address))
        {
            return true;
        }

        var bits = Width(range.Address) - range.Cidr;
        if (bits < 2)
        {
            return false;
        }

        return number == UInt128.Zero || (!range.IsSix && number == (UInt128.One << bits) - UInt128.One);
    }

    private static UInt128 Number(AwgAllowedIp range, IPAddress address) =>
        Value(address) - Value(range.Network().Address);

    private static IPAddress Nth(AwgAllowedIp range, UInt128 number) =>
        Point(Value(range.Network().Address) + number, range.IsSix);

    private static UInt128 Value(IPAddress address)
    {
        var value = UInt128.Zero;
        foreach (var one in address.GetAddressBytes())
        {
            value = (value << 8) | one;
        }

        return value;
    }

    private static IPAddress Point(UInt128 value, bool six)
    {
        var bytes = new byte[six ? 16 : 4];
        for (var at = bytes.Length - 1; at >= 0; at--)
        {
            bytes[at] = (byte)(value & byte.MaxValue);
            value >>= 8;
        }

        return new IPAddress(bytes);
    }

    private static HashSet<IPAddress> Held(IEnumerable<string> taken)
    {
        var held = new HashSet<IPAddress>();
        foreach (var text in taken)
        {
            if (AwgAllowedIp.TryParse(text, out var range))
            {
                held.Add(range.Address);
            }
        }

        return held;
    }

    private static ulong Room(AwgAllowedIp range)
    {
        var width = Width(range.Address);
        var left = width - range.Cidr;

        return left >= 32 ? MaxSearch : Math.Min(1UL << left, MaxSearch);
    }

    private static void Mask(byte[] bytes, byte cidr)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            var bits = cidr - (index * 8);
            bytes[index] = bits >= 8 ? bytes[index] : bits <= 0 ? (byte)0 : (byte)(bytes[index] & (0xFF << (8 - bits)));
        }
    }

    private static byte Width(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)128 : (byte)32;
}
