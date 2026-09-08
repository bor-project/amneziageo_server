namespace AmneziaGeo.Server.Geo;

/// <summary>
/// The kinds of database a geo source carries.
/// </summary>
public static class GeoKind
{
    /// <summary>
    /// Address ranges under a country code.
    /// </summary>
    public const string Ip = "geoip";

    /// <summary>
    /// Domains under a category code.
    /// </summary>
    public const string Site = "geosite";

    /// <summary>
    /// Every kind a source can carry.
    /// </summary>
    public static readonly string[] All = [Ip, Site];

    /// <summary>
    /// Tells whether a name is a kind the server reads.
    /// </summary>
    public static bool Known(string kind) =>
        kind.Equals(Ip, StringComparison.OrdinalIgnoreCase) || kind.Equals(Site, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tells whether a source carries address ranges.
    /// </summary>
    public static bool IsIp(string kind) => kind.Equals(Ip, StringComparison.OrdinalIgnoreCase);
}
