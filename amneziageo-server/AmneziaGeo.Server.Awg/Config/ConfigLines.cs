using System.Globalization;

namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// One section of an interface file with the name written above it.
/// </summary>
/// <param name="Title">What the comment above the section said.</param>
/// <param name="Values">The settings the section carries.</param>
public sealed record ConfigBlock(string Title, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// Reads the sections and settings of an interface file.
/// </summary>
public static class ConfigLines
{
    private static readonly char[] Breaks = [',', ' ', '\t'];

    /// <summary>
    /// Returns the settings of the interface itself.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Head(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var head = Fresh();
        var current = head;
        foreach (var raw in text.Split('\n'))
        {
            var line = Trim(raw);
            if (line.StartsWith('['))
            {
                current = line.StartsWith("[Interface", StringComparison.OrdinalIgnoreCase) ? head : Fresh();

                continue;
            }

            Take(current, line);
        }

        return head;
    }

    /// <summary>
    /// Returns every peer section with the comment written above it.
    /// </summary>
    public static IReadOnlyList<ConfigBlock> Blocks(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var blocks = new List<ConfigBlock>();
        var values = Fresh();
        var title = string.Empty;
        var comment = string.Empty;
        var inside = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#'))
            {
                comment = line[1..].Trim();

                continue;
            }

            if (line.StartsWith('['))
            {
                if (inside)
                {
                    blocks.Add(new ConfigBlock(title, values));
                }

                inside = line.StartsWith("[Peer", StringComparison.OrdinalIgnoreCase);
                values = Fresh();
                title = comment;
                comment = string.Empty;

                continue;
            }

            Take(values, Trim(raw));
        }

        if (inside)
        {
            blocks.Add(new ConfigBlock(title, values));
        }

        return blocks;
    }

    /// <summary>
    /// Returns one setting, or an empty string when the section carries none.
    /// </summary>
    public static string Value(IReadOnlyDictionary<string, string> section, string key)
    {
        ArgumentNullException.ThrowIfNull(section);

        return section.TryGetValue(key, out var found) ? found : string.Empty;
    }

    /// <summary>
    /// Returns one setting as a number, or what it falls back to.
    /// </summary>
    public static int Number(IReadOnlyDictionary<string, string> section, string key, int fallback)
    {
        ArgumentNullException.ThrowIfNull(section);

        return section.TryGetValue(key, out var found)
            && int.TryParse(Lower(found), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
    }

    /// <summary>
    /// Returns one setting as a switch.
    /// </summary>
    public static bool Flag(IReadOnlyDictionary<string, string> section, string key)
    {
        ArgumentNullException.ThrowIfNull(section);

        var value = Value(section, key);

        return value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns one setting split into the values it lists.
    /// </summary>
    public static IReadOnlyList<string> Parts(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static Dictionary<string, string> Fresh() => new(StringComparer.OrdinalIgnoreCase);

    private static void Take(Dictionary<string, string> section, string line)
    {
        var mark = line.IndexOf('=', StringComparison.Ordinal);
        if (mark > 0)
        {
            section[line[..mark].Trim()] = line[(mark + 1)..].Trim();
        }
    }

    private static string Lower(string value)
    {
        var mark = value.IndexOf('-', StringComparison.Ordinal);

        return mark <= 0 ? value.Trim() : value[..mark].Trim();
    }

    private static string Trim(string line)
    {
        var body = line.Trim();
        var mark = body.IndexOf('#', StringComparison.Ordinal);

        return mark < 0 ? body : body[..mark].Trim();
    }
}
