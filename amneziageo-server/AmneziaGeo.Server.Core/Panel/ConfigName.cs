using System.Text;

namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// Names the configurations of the clients by the template of the panel.
/// </summary>
public static class ConfigName
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

    private const string Unsafe = @"\/:*?""<>|";

    private const string Bare = "config";

    /// <summary>
    /// Returns the substitutions of a template the panel does not know.
    /// </summary>
    public static IReadOnlyList<string> Unknown(string template)
    {
        return NamePattern.Unknown(template, Keys);
    }

    /// <summary>
    /// Returns the name a template gives with the values of its substitutions, empty when nothing is left of it.
    /// </summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values)
    {
        return NamePattern.Fill(template, values, Keys);
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

}
