using System.Net;

namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// Finds the domain a name belongs to.
/// </summary>
public static class AccessDomain
{
    private static readonly HashSet<string> Seconds = new(StringComparer.Ordinal)
    {
        "ac", "co", "com", "edu", "go", "gob", "gov", "ltd", "me", "mil", "ne", "net", "or", "org", "plc", "sch",
    };

    /// <summary>
    /// Returns the last two labels of a name, three under a country with second level domains, the address as is.
    /// </summary>
    public static string Of(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0 || IPAddress.TryParse(name, out _))
        {
            return name;
        }

        var labels = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 2)
        {
            return name;
        }

        var take = labels[^1].Length == 2 && Seconds.Contains(labels[^2]) ? 3 : 2;

        return string.Join('.', labels[^take..]);
    }
}
