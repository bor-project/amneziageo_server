using System.Buffers.Binary;
using System.Text;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// Reads the attribute stream that follows a netlink header.
/// </summary>
public static class NetlinkAttributes
{
    /// <summary>
    /// Walks the attributes of a payload in order.
    /// </summary>
    public static IEnumerable<(ushort Type, ReadOnlyMemory<byte> Value)> Walk(ReadOnlyMemory<byte> payload)
    {
        var offset = 0;
        while (offset + 4 <= payload.Length)
        {
            var span = payload.Span;
            var length = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
            var type = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 2)..]);
            if (length < 4 || offset + length > payload.Length)
            {
                yield break;
            }

            yield return ((ushort)(type & 0x3FFF), payload.Slice(offset + 4, length - 4));
            offset += (length + 3) & ~3;
        }
    }

    /// <summary>
    /// Collects the attributes of a payload into a map, keeping the last of a repeated type.
    /// </summary>
    public static Dictionary<ushort, ReadOnlyMemory<byte>> Map(ReadOnlyMemory<byte> payload)
    {
        var map = new Dictionary<ushort, ReadOnlyMemory<byte>>();
        foreach (var (type, value) in Walk(payload))
        {
            map[type] = value;
        }

        return map;
    }

    /// <summary>
    /// Reads a 16 bit number, or null when the attribute is absent or short.
    /// </summary>
    public static ushort? U16(Dictionary<ushort, ReadOnlyMemory<byte>> map, ushort type)
        => map.TryGetValue(type, out var value) && value.Length >= 2
            ? BinaryPrimitives.ReadUInt16LittleEndian(value.Span)
            : null;

    /// <summary>
    /// Reads a 32 bit number, or null when the attribute is absent or short.
    /// </summary>
    public static uint? U32(Dictionary<ushort, ReadOnlyMemory<byte>> map, ushort type)
        => map.TryGetValue(type, out var value) && value.Length >= 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(value.Span)
            : null;

    /// <summary>
    /// Reads a 64 bit number, or null when the attribute is absent or short.
    /// </summary>
    public static ulong? U64(Dictionary<ushort, ReadOnlyMemory<byte>> map, ushort type)
        => map.TryGetValue(type, out var value) && value.Length >= 8
            ? BinaryPrimitives.ReadUInt64LittleEndian(value.Span)
            : null;

    /// <summary>
    /// Reads a number of whatever width the kernel wrote, or null when the attribute is absent.
    /// </summary>
    public static ulong? Number(Dictionary<ushort, ReadOnlyMemory<byte>> map, ushort type)
    {
        if (!map.TryGetValue(type, out var value))
        {
            return null;
        }

        var span = value.Span;

        return span.Length switch
        {
            >= 8 => BinaryPrimitives.ReadUInt64LittleEndian(span),
            >= 4 => BinaryPrimitives.ReadUInt32LittleEndian(span),
            >= 2 => BinaryPrimitives.ReadUInt16LittleEndian(span),
            1 => span[0],
            _ => null,
        };
    }

    /// <summary>
    /// Reads a null terminated string, or null when the attribute is absent.
    /// </summary>
    public static string? Text(Dictionary<ushort, ReadOnlyMemory<byte>> map, ushort type)
    {
        if (!map.TryGetValue(type, out var value))
        {
            return null;
        }

        var span = value.Span;
        var end = span.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? span : span[..end]);
    }
}
