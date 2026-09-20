namespace AmneziaGeo.Server.Geo;

/// <summary>
/// What a routing rule matches traffic by.
/// </summary>
public enum GeoRuleKind
{
    /// <summary>
    /// A geosite category, as in geosite:youtube.
    /// </summary>
    GeoSite,

    /// <summary>
    /// A geoip country, as in geoip:ru.
    /// </summary>
    GeoIp,

    /// <summary>
    /// A single domain suffix.
    /// </summary>
    Domain,

    /// <summary>
    /// A word a name carries, as in keyword:ads.
    /// </summary>
    Keyword,

    /// <summary>
    /// A single address range in CIDR notation.
    /// </summary>
    Cidr,
}

/// <summary>
/// One condition of a routing rule.
/// </summary>
/// <param name="Kind">What the value is matched as.</param>
/// <param name="Value">The category, country, name or range.</param>
public sealed record GeoRule(GeoRuleKind Kind, string Value);
