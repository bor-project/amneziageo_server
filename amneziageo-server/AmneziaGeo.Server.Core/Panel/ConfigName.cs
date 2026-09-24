using System.Text;
using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// Names the configurations of the clients by the template of the panel.
/// </summary>
public static partial class ConfigName
{
    /// <summary>
    /// The template the panel names the configurations by when none is set.
    /// </summary>
    public const string Default = "{HOST}-{INTERFACE}-{CLIENT}";

    /// <summary>
    /// The longest a template is.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// The substitutions a template takes.
    /// </summary>
    public static readonly IReadOnlyList<string> Keys = ["HOST", "INTERFACE", "CLIENT", "ID", "PORT", "NOTE", "DATE"];

    private const string Separators = "-_.| ";

    private const string Unsafe = @"\/:*?""<>|";

    private const string Bare = "config";

    /// <summary>
    /// Returns the substitutions of a template the panel does not know.
    /// </summary>
    public static IReadOnlyList<string> Unknown(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return [.. Parts(template).Where(part => part.Key is { } key && Known(key) is null).Select(part => part.Text)];
    }

    /// <summary>
    /// Returns the name a template gives with the values of its substitutions, empty when nothing is left of it.
    /// </summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        var name = new StringBuilder();
        var dropping = false;
        foreach (var part in Parts(template))
        {
            if (part.Key is { } key && Known(key) is { } known)
            {
                var value = values.GetValueOrDefault(known, string.Empty).Trim();
                if (value.Length == 0)
                {
                    // An empty value takes the separator after it along.
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

    /// <summary>
    /// Returns a name with the characters a file name does not take replaced.
    /// </summary>
    public static string File(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var file = new StringBuilder(name.Length);
        foreach (var letter in name)
        {
            file.Append(char.IsControl(letter) || Unsafe.Contains(letter) ? '_' : letter);
        }

        var trimmed = file.ToString().TrimEnd('.', ' ');

        return trimmed.Length > 0 ? trimmed : Bare;
    }

    private static string? Known(string key)
    {
        var bare = key.Trim();

        return Keys.FirstOrDefault(one => string.Equals(one, bare, StringComparison.OrdinalIgnoreCase));
    }

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
