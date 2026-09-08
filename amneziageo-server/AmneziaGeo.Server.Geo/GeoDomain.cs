namespace AmneziaGeo.Server.Geo;

/// <summary>
/// How a geosite entry is matched against a name.
/// </summary>
public enum GeoDomainKind
{
    /// <summary>
    /// Substring keyword match.
    /// </summary>
    Plain,

    /// <summary>
    /// Regular expression match.
    /// </summary>
    Regex,

    /// <summary>
    /// Domain suffix match.
    /// </summary>
    Domain,

    /// <summary>
    /// Exact domain match.
    /// </summary>
    Full,
}

/// <summary>
/// One domain rule of a geosite category.
/// </summary>
/// <param name="Kind">How the value is matched.</param>
/// <param name="Value">The name, keyword or expression.</param>
public sealed record GeoDomain(GeoDomainKind Kind, string Value);
