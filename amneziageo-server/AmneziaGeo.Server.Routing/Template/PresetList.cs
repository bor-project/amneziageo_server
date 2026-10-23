using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Routing.Template;

/// <summary>
/// Reads the entries of a routing preset: the entries of a template and the applications the client finds itself.
/// </summary>
public static class PresetList
{
    /// <summary>
    /// The word an entry that names an application starts with.
    /// </summary>
    public const string AppMark = "app:";

    /// <summary>
    /// The longest application an entry names.
    /// </summary>
    public const int MaxAppLength = 260;

    private static readonly string[] AppKinds = ["path", "dir", "name", "svc", "pkg"];

    // The kinds an entry may be led by.
    private static readonly string[] Kinds = [AppMark, "geoip:", "geosite:", "domain:", "cidr:"];

    // Where the entries of a line part.
    private static readonly char[] Parts = [',', ';'];

    private static readonly char[] Spaces = [' ', '\t'];

    // What is cut off the edges of a piece of a line.
    private static readonly char[] Edges = [' ', '\t', '\r', ',', ';'];

    /// <summary>
    /// Returns the entry written the way a preset keeps it, or null when the text is none.
    /// </summary>
    public static string? Entry(string? text) => IsApp(text) ? App(text!) : TemplateList.Entry(text);

    /// <summary>
    /// Tells whether the text names an application.
    /// </summary>
    public static bool IsApp(string? text) =>
        (text ?? string.Empty).TrimStart().StartsWith(AppMark, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the pieces of a line: an application runs from its app: to a comma or a semicolon another entry
    /// follows, the text around the applications stays as it is.
    /// </summary>
    public static IReadOnlyList<string> Pieces(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var pieces = new List<string>();
        var start = 0;
        var app = false;
        for (var at = 0; at < line.Length; at++)
        {
            if (!app)
            {
                if (AppAt(line, at))
                {
                    pieces.Add(line[start..at]);
                    start = at;
                    app = true;
                }

                continue;
            }

            if (line[at] is not (',' or ';'))
            {
                continue;
            }

            var next = line.IndexOfAny(Parts, at + 1);
            if (StartsEntry(line[(at + 1)..(next < 0 ? line.Length : next)]))
            {
                pieces.Add(line[start..at]);
                start = at + 1;
                app = false;
            }
        }

        pieces.Add(line[start..]);

        return [.. pieces.Select(piece => piece.Trim(Edges)).Where(piece => piece.Length > 0)];
    }

    /// <summary>
    /// Returns why the entries of a preset are unusable, or null when they hold.
    /// </summary>
    public static ClientFault? Check(IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count > TemplateList.MaxEntries)
        {
            return new ClientFault("too-many-entries", $"a preset carries at most {TemplateList.MaxEntries} entries in a list");
        }

        foreach (var entry in entries)
        {
            if (Entry(entry) is null)
            {
                return new ClientFault("bad-entry", $"'{entry}' is not a geo key, a network, an address, a domain or an application");
            }
        }

        return null;
    }

    // Whether an application starts at this place of a line: app: at its start or after a space or a separator.
    private static bool AppAt(string line, int at) =>
        (at == 0 || char.IsWhiteSpace(line[at - 1]) || line[at - 1] is ',' or ';')
        && string.Compare(line, at, AppMark, 0, AppMark.Length, StringComparison.OrdinalIgnoreCase) == 0;

    // Whether a text starts another entry: one led by its kind, or an address, a network or a domain with a dot.
    private static bool StartsEntry(string text)
    {
        var body = text.Trim();
        if (Kinds.Any(kind => body.StartsWith(kind, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var word = body.Split(Spaces, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        return (word.Contains('.', StringComparison.Ordinal) || word.Contains(':', StringComparison.Ordinal)) && TemplateList.Entry(word) is not null;
    }

    // Returns an application entry, app:<name> or app:<kind>=<value>, or null when the text names none.
    private static string? App(string text)
    {
        var body = text.Trim()[AppMark.Length..].Trim();
        if (body.Length is 0 or > MaxAppLength || body.Any(letter => char.IsControl(letter) || letter == '|'))
        {
            return null;
        }

        var equals = body.IndexOf('=', StringComparison.Ordinal);
        if (equals < 0)
        {
            return AppMark + body;
        }

        var kind = body[..equals].Trim().ToLowerInvariant();
        var value = body[(equals + 1)..].Trim();

        return equals > 0 && value.Length > 0 && AppKinds.Contains(kind) ? $"{AppMark}{kind}={value}" : null;
    }
}
