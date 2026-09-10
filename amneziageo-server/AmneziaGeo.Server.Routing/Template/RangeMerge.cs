using System.Buffers.Binary;
using System.Net;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Routing.Template;

/// <summary>
/// Folds address ranges into the fewest that cover the same addresses.
/// </summary>
public static class RangeMerge
{
    /// <summary>
    /// Returns the ranges with every overlap and every seam between neighbours folded, the first family ahead.
    /// </summary>
    public static IReadOnlyList<AwgAllowedIp> Merge(IEnumerable<AwgAllowedIp> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var all = ranges.ToArray();

        return [.. Fold(all.Where(range => !range.IsSix), 32), .. Fold(all.Where(range => range.IsSix), 128)];
    }

    private static IEnumerable<AwgAllowedIp> Fold(IEnumerable<AwgAllowedIp> ranges, int width)
    {
        var merged = new List<(UInt128 Low, UInt128 High)>();
        foreach (var span in ranges.Select(range => Span(range, width)).OrderBy(span => span.Low))
        {
            if (merged.Count > 0 && Touches(merged[^1], span))
            {
                var last = merged[^1];
                merged[^1] = (last.Low, UInt128.Max(last.High, span.High));
                continue;
            }

            merged.Add(span);
        }

        return merged.SelectMany(span => Blocks(span.Low, span.High, width));
    }

    private static bool Touches((UInt128 Low, UInt128 High) last, (UInt128 Low, UInt128 High) next) =>
        last.High == UInt128.MaxValue || next.Low <= last.High + 1;

    private static (UInt128 Low, UInt128 High) Span(AwgAllowedIp range, int width)
    {
        var low = Number(range.Network().Address);

        return (low, End(low, width - range.Cidr));
    }

    private static IEnumerable<AwgAllowedIp> Blocks(UInt128 low, UInt128 high, int width)
    {
        var at = low;
        while (true)
        {
            var bits = Math.Min(width, (int)UInt128.TrailingZeroCount(at));
            while (bits > 0 && End(at, bits) > high)
            {
                bits--;
            }

            yield return new AwgAllowedIp(Address(at, width), (byte)(width - bits));

            var end = End(at, bits);
            if (end >= high)
            {
                yield break;
            }

            at = end + 1;
        }
    }

    private static UInt128 End(UInt128 start, int bits) =>
        bits >= 128 ? UInt128.MaxValue : start + ((UInt128.One << bits) - 1);

    private static UInt128 Number(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        return bytes.Length == 4
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes)
            : BinaryPrimitives.ReadUInt128BigEndian(bytes);
    }

    private static IPAddress Address(UInt128 value, int width)
    {
        if (width == 32)
        {
            var four = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(four, (uint)value);

            return new IPAddress(four);
        }

        var sixteen = new byte[16];
        BinaryPrimitives.WriteUInt128BigEndian(sixteen, value);

        return new IPAddress(sixteen);
    }
}
