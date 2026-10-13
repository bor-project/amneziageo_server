namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// How far the first bytes of a connection went towards its name.
/// </summary>
public enum NameReading
{
    /// <summary>
    /// The bytes end before the name does.
    /// </summary>
    More = 0,

    /// <summary>
    /// The name was read.
    /// </summary>
    Found = 1,

    /// <summary>
    /// The bytes carry no name.
    /// </summary>
    None = 2,
}

/// <summary>
/// What the first bytes of a connection tell about the name it goes to.
/// </summary>
/// <param name="Reading">Whether the name was read.</param>
/// <param name="Name">The name in lower case, empty when there is none.</param>
public readonly record struct SeenName(NameReading Reading, string Name)
{
    /// <summary>
    /// The bytes end before the name does.
    /// </summary>
    public static SeenName More => new(NameReading.More, string.Empty);

    /// <summary>
    /// The bytes carry no name.
    /// </summary>
    public static SeenName None => new(NameReading.None, string.Empty);

    /// <summary>
    /// Returns the reading of bytes that carry a name, none when it is not a host name.
    /// </summary>
    public static SeenName Of(ReadOnlySpan<byte> name)
    {
        if (name.Length == 0 || name.Length > 253)
        {
            return None;
        }

        Span<char> text = stackalloc char[name.Length];
        for (var at = 0; at < name.Length; at++)
        {
            var one = (char)name[at];
            if (one is >= 'A' and <= 'Z')
            {
                one = (char)(one + 32);
            }

            if (one is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_'))
            {
                return None;
            }

            text[at] = one;
        }

        var trimmed = text.TrimEnd('.');

        return trimmed.Length == 0 ? None : new SeenName(NameReading.Found, new string(trimmed));
    }
}
