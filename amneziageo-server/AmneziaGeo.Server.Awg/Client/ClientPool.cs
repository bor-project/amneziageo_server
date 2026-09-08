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
    /// Returns a free address out of every range of an endpoint.
    /// </summary>
    public static IReadOnlyList<string> Free(IReadOnlyList<string> ranges, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(taken);

        var held = Held(taken);
        var picked = new List<string>();
        foreach (var text in ranges)
        {
            if (!AwgAllowedIp.TryParse(text, out var range))
            {
                continue;
            }

            var address = Pick(range, held);
            if (address is null)
            {
                continue;
            }

            held.Add(address);
            picked.Add($"{address}/{Width(address)}");
        }

        return picked;
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

    private static IPAddress? Pick(AwgAllowedIp range, HashSet<IPAddress> held)
    {
        var bytes = range.Address.GetAddressBytes();
        Mask(bytes, range.Cidr);
        var room = Room(range);
        for (var offset = 2UL; offset < room; offset++)
        {
            var candidate = new IPAddress(Shift(bytes, offset));
            if (!held.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
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

    private static byte[] Shift(byte[] bytes, ulong offset)
    {
        var moved = (byte[])bytes.Clone();
        for (var index = moved.Length - 1; index >= 0 && offset > 0; index--)
        {
            var sum = moved[index] + (offset & 0xFF);
            moved[index] = (byte)sum;
            offset = (offset >> 8) + (ulong)(sum >> 8);
        }

        return moved;
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
