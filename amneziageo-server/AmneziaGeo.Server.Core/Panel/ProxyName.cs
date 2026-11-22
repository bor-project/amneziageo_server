namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// Formats published proxy names.
/// </summary>
public static class ProxyName
{
    public const string Default = "{PROXY}";

    public const int MaxLength = 128;

    public const int MaxNameLength = 64;

    public static readonly IReadOnlyList<string> Keys = ["HOST", "PROXY", "TRANSPORT", "ID", "PORT", "PATH"];

    /// <summary>
    /// Lists unknown substitutions.
    /// </summary>
    public static IReadOnlyList<string> Unknown(string template) => NamePattern.Unknown(template, Keys);

    /// <summary>
    /// Formats a proxy label.
    /// </summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        var original = values.GetValueOrDefault("PROXY", string.Empty);
        var filled = template == Default ? original : NamePattern.Fill(template, values, Keys);
        var name = filled.Length > 0 ? filled : original;
        var length = Math.Min(name.Length, MaxNameLength);
        if (length < name.Length && char.IsHighSurrogate(name[length - 1]))
        {
            length--;
        }

        return name[..length];
    }
}
