using System.Globalization;

namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// Checks a special packet description.
/// </summary>
public static class CpsPacket
{
    /// <summary>
    /// The most tags in a packet description.
    /// </summary>
    public const int MaxTags = 512;

    /// <summary>
    /// The largest IPv4 UDP payload.
    /// </summary>
    public const int MaxPacketLength = 65507;

    /// <summary>
    /// Returns whether the description fits the packet limits.
    /// </summary>
    public static bool IsValid(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return true;
        }

        if (description.Length > ConfigRules.MaxSpecialLength)
        {
            return false;
        }

        var remaining = description.AsSpan().Trim();
        if (remaining.Length >= 2 && remaining[0] is '"' or '\'' && remaining[^1] == remaining[0])
        {
            remaining = remaining[1..^1].Trim();
        }

        var count = 0;
        var length = 0;
        while (!remaining.IsEmpty)
        {
            if (++count > MaxTags || remaining[0] != '<')
            {
                return false;
            }

            var end = remaining.IndexOf('>');
            if (end < 0)
            {
                return false;
            }

            var tag = remaining[1..end];
            var size = Size(tag);
            if (size < 0 || size > MaxPacketLength - length)
            {
                return false;
            }

            length += size;
            remaining = remaining[(end + 1)..].TrimStart();
        }

        return true;
    }

    private static int Size(ReadOnlySpan<char> tag)
    {
        if (tag.SequenceEqual("t") || tag.SequenceEqual("c"))
        {
            return 4;
        }

        var separator = tag.IndexOf(' ');
        if (separator < 0)
        {
            return -1;
        }

        var kind = tag[..separator];
        var value = tag[(separator + 1)..];
        if (kind.SequenceEqual("b"))
        {
            if (!value.StartsWith("0x", StringComparison.Ordinal) || value.Length < 4 || value.Length % 2 != 0)
            {
                return -1;
            }

            foreach (var digit in value[2..])
            {
                if (!char.IsAsciiHexDigit(digit))
                {
                    return -1;
                }
            }

            return (value.Length - 2) / 2;
        }

        return (kind.SequenceEqual("r") || kind.SequenceEqual("rc") || kind.SequenceEqual("rd"))
            && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var size) && size >= 0
                ? size
                : -1;
    }
}
