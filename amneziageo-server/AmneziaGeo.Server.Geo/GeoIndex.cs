using AmneziaGeo.Server.Geo.Files;
using AmneziaGeo.Server.Geo.Format;

namespace AmneziaGeo.Server.Geo;

/// <summary>
/// One view over the geo sources the server holds; a later source overrides an earlier one per entry.
/// </summary>
public sealed class GeoIndex
{
    private readonly IGeoFileStore _files;
    private readonly List<string> _geoip;
    private readonly List<string> _geosite;
    private readonly Dictionary<string, IReadOnlyList<string>> _cidrs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<GeoDomain>> _domains = new(StringComparer.OrdinalIgnoreCase);

    private GeoIndex(IGeoFileStore files, List<string> geoip, List<string> geosite)
    {
        _files = files;
        _geoip = geoip;
        _geosite = geosite;
    }

    /// <summary>
    /// Takes the sources that are on, ordered by position. The files stay on disk and are read per query.
    /// </summary>
    public static GeoIndex Load(IReadOnlyList<GeoSource> sources, IGeoFileStore files)
    {
        var geoip = new List<string>();
        var geosite = new List<string>();
        foreach (var source in sources.Where(source => source.IsEnabled).OrderBy(source => source.Position))
        {
            if (GeoKind.IsIp(source.Kind))
            {
                geoip.Add(source.Name);
            }
            else
            {
                geosite.Add(source.Name);
            }
        }

        return new GeoIndex(files, geoip, geosite);
    }

    /// <summary>
    /// Returns the ranges of a country from the last source that carries it.
    /// </summary>
    public IReadOnlyList<string> Cidrs(string country)
    {
        if (_cidrs.TryGetValue(country, out var held))
        {
            return held;
        }

        IReadOnlyList<string> found = [];
        foreach (var name in _geoip)
        {
            var cidrs = Read(name, stream => GeoIpDatabase.Cidrs(stream, country));
            if (cidrs.Count > 0)
            {
                found = cidrs;
            }
        }

        _cidrs[country] = found;
        return found;
    }

    /// <summary>
    /// Returns the domains of a category from the last source that carries it.
    /// </summary>
    public IReadOnlyList<GeoDomain> Domains(string category)
    {
        if (_domains.TryGetValue(category, out var held))
        {
            return held;
        }

        IReadOnlyList<GeoDomain> found = [];
        foreach (var name in _geosite)
        {
            var domains = Read(name, stream => GeoSiteDatabase.Domains(stream, category));
            if (domains.Count > 0)
            {
                found = domains;
            }
        }

        _domains[category] = found;
        return found;
    }

    /// <summary>
    /// Returns every category code across the sources that are on.
    /// </summary>
    public IReadOnlyList<string> Categories() => Codes(_geosite, GeoSiteDatabase.Categories);

    /// <summary>
    /// Returns every country code across the sources that are on.
    /// </summary>
    public IReadOnlyList<string> Countries() => Codes(_geoip, GeoIpDatabase.Countries);

    private IReadOnlyList<string> Codes(List<string> names, Func<Stream, IReadOnlyList<string>> read)
    {
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            foreach (var code in Read(name, read))
            {
                if (code.Length > 0)
                {
                    codes.Add(code);
                }
            }
        }

        return [.. codes];
    }

    // A missing or unreadable file yields nothing, and the other sources still answer.
    private IReadOnlyList<T> Read<T>(string name, Func<Stream, IReadOnlyList<T>> read)
    {
        using var stream = _files.OpenRead(name);
        if (stream is null)
        {
            return [];
        }

        try
        {
            return read(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or FormatException or EndOfStreamException or NotSupportedException or IOException)
        {
            return [];
        }
    }
}
