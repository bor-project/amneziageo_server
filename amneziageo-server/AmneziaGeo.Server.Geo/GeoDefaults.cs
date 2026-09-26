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
    /// The number of the set of sources below, raised when a source joins it.
    /// </summary>
    public const int Version = 2;

    private static readonly Dictionary<string, int> Joined = new(StringComparer.Ordinal) { ["zkeenip"] = 2 };

    /// <summary>
    /// The sources in the order they override each other in.
    /// </summary>
    public static readonly GeoSource[] Sources =
    [
        new()
        {
            Name = "zkeenip",
            Kind = GeoKind.Ip,
            Url = "https://github.com/jameszeroX/zkeen-ip/releases/latest/download/zkeenip.dat",
            Position = 1,
        },
        new()
        {
            Name = "geosite",
            Kind = GeoKind.Site,
            Url = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/latest/download/geosite.dat",
            Position = 2,
        },
        new()
        {
            Name = "geoip",
            Kind = GeoKind.Ip,
            Url = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/latest/download/geoip.dat",
            Position = 3,
        },
        new()
        {
            Name = "geosite-ru-only",
            Kind = GeoKind.Site,
            Url = "https://github.com/runetfreedom/russia-blocked-geosite/releases/latest/download/geosite-ru-only.dat",
            Position = 4,
        },
        new()
        {
            Name = "geoip-ru-only",
            Kind = GeoKind.Ip,
            Url = "https://github.com/runetfreedom/russia-blocked-geoip/releases/latest/download/geoip-ru-only.dat",
            Position = 5,
        },
    ];

    /// <summary>
    /// Returns the number of the set a source joined in.
    /// </summary>
    public static int Since(GeoSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Joined.GetValueOrDefault(source.Name, 1);
    }
}
