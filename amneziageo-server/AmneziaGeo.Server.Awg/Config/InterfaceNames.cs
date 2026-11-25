using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AmneziaGeo.Server.Awg.Config;

public static class InterfaceNames
{
    /// <summary>
    /// Identifies characters unsupported by interface names and name lists.
    /// </summary>
    public static bool Forbidden(char value) => char.IsWhiteSpace(value) || char.IsControl(value) || "/:,\"\\*%".Contains(value);

    /// <summary>
    /// Suggests an available interface name from an address.
    /// </summary>
    public static string Suggest(string? address, IEnumerable<string> taken, string fallback)
    {
        var source = (address ?? string.Empty).Trim().Split('/')[0].Trim('[', ']');
        var text = new StringBuilder();
        foreach (var value in source)
        {
            var letter = Forbidden(value) ? '-' : value;
            if (letter != '-' || text.Length == 0 || text[^1] != '-')
            {
                text.Append(letter);
            }
        }
        var name = text.ToString().Trim('-', '.');
        if (name.Length == 0)
        {
            name = fallback;
        }
        if (Encoding.UTF8.GetByteCount(name) > ConfigRules.MaxNameLength)
        {
            var suffix = "-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..6];
            name = Prefix(name, ConfigRules.MaxNameLength - suffix.Length) + suffix;
        }
        var reserved = taken.ToHashSet(StringComparer.Ordinal);
        var candidate = name;
        for (var ordinal = 2; reserved.Contains(candidate); ordinal++)
        {
            var suffix = "-" + ordinal.ToString(CultureInfo.InvariantCulture);
            candidate = Prefix(name, ConfigRules.MaxNameLength - suffix.Length) + suffix;
        }
        return candidate;
    }

    private static string Prefix(string text, int bytes)
    {
        var result = new StringBuilder();
        var length = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            length += rune.Utf8SequenceLength;
            if (length > bytes)
            {
                break;
            }
            result.Append(rune.ToString());
        }
        return result.ToString();
    }
}
