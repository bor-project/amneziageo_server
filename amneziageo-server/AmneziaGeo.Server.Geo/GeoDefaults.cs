namespace AmneziaGeo.Server.Geo;

/// <summary>
/// The geo sources a fresh install starts with.
/// </summary>
public static class GeoDefaults
{
    /// <summary>
    /// How often the sources are downloaded again.
    /// </summary>
    public static readonly TimeSpan UpdateEvery = TimeSpan.FromHours(24);

    /// <summary>
    /// The sources in the order they override each other in.
    /// </summary>
    public static readonly GeoSource[] Sources =
    [
        new()
        {
            Name = "geosite",
            Kind = GeoKind.Site,
            Url = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/latest/download/geosite.dat",
            Position = 1,
        },
        new()
        {
            Name = "geoip",
            Kind = GeoKind.Ip,
            Url = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/latest/download/geoip.dat",
            Position = 2,
        },
        new()
        {
            Name = "geosite-ru-only",
            Kind = GeoKind.Site,
            Url = "https://github.com/runetfreedom/russia-blocked-geosite/releases/latest/download/geosite-ru-only.dat",
            Position = 3,
        },
        new()
        {
            Name = "geoip-ru-only",
            Kind = GeoKind.Ip,
            Url = "https://github.com/runetfreedom/russia-blocked-geoip/releases/latest/download/geoip-ru-only.dat",
            Position = 4,
        },
    ];
}
