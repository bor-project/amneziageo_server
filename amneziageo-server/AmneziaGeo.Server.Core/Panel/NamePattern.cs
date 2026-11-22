using System.Text;
using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// Formats name substitutions.
/// </summary>
internal static partial class NamePattern
{
    private const string Separators = "-_.| ";

    internal static IReadOnlyList<string> Unknown(string template, IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(template);

        return [.. Parts(template).Where(part => part.Key is { } key && Known(key, keys) is null).Select(part => part.Text)];
    }

    internal static string Fill(string template, IReadOnlyDictionary<string, string> values, IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        var name = new StringBuilder();
        var dropping = false;
        foreach (var part in Parts(template))
        {
            if (part.Key is { } key && Known(key, keys) is { } known)
            {
                var value = values.GetValueOrDefault(known, string.Empty).Trim();
                if (value.Length == 0)
                {
                    dropping = true;
                    continue;
                }

                name.Append(value);
                dropping = false;
                continue;
            }

            var text = part.Text;
            if (dropping && Separators.Contains(text[0]))
            {
                text = text[1..];
            }

            name.Append(text);
            dropping = false;
        }

        var line = new StringBuilder(name.Length);
        foreach (var letter in name.ToString())
        {
            if (!char.IsControl(letter))
            {
                line.Append(letter);
            }
        }

        return line.ToString().Trim(Separators.ToCharArray());
    }

    private static string? Known(string key, IReadOnlyList<string> keys) =>
        keys.FirstOrDefault(one => string.Equals(one, key.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Part> Parts(string template)
    {
        var at = 0;
        foreach (Match match in Slot().Matches(template))
        {
            if (match.Index > at)
            {
                yield return new Part(template[at..match.Index], null);
            }

            yield return new Part(match.Value, match.Groups[1].Value);
            at = match.Index + match.Length;
        }

        if (at < template.Length)
        {
            yield return new Part(template[at..], null);
        }
    }

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex Slot();

    private sealed record Part(string Text, string? Key);
}
