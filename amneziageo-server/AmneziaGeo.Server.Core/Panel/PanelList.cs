namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// Turns a list the settings hold into a line and back.
/// </summary>
public static class PanelList
{
    /// <summary>
    /// What stands between the items of a line.
    /// </summary>
    public const char Mark = ';';

    /// <summary>
    /// Returns the items a line carries.
    /// </summary>
    public static IReadOnlyList<string> Split(string? line) => line is null ? [] : Of([line]);

    /// <summary>
    /// Returns the items without the blanks and the repeats.
    /// </summary>
    public static IReadOnlyList<string> Of(IEnumerable<string>? items)
    {
        if (items is null)
        {
            return [];
        }

        var found = new List<string>();
        foreach (var piece in items.SelectMany(item => item.Split(Mark)))
        {
            var item = piece.Trim();
            if (item.Length > 0 && !found.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                found.Add(item);
            }
        }

        return found;
    }

    /// <summary>
    /// Returns the line the items make.
    /// </summary>
    public static string Line(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return string.Join(Mark, items);
    }
}
