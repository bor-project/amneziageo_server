namespace AmneziaGeo.Server.Geo;

/// <summary>
/// What a set of rules came out as once the databases were read.
/// </summary>
/// <param name="Cidrs">The address ranges the rules name.</param>
/// <param name="Domains">The domains the rules name.</param>
public sealed record GeoTargets(IReadOnlyList<string> Cidrs, IReadOnlyList<GeoDomain> Domains);

/// <summary>
/// Turns rules into the ranges and names they stand for.
/// </summary>
public static class GeoMaterializer
{
    /// <summary>
    /// Expands a set of rules over the index.
    /// </summary>
    public static GeoTargets Materialize(IReadOnlyList<GeoRule> rules, GeoIndex index)
    {
        var cidrs = new List<string>();
        var domains = new List<GeoDomain>();

        foreach (var rule in rules)
        {
            switch (rule.Kind)
            {
                case GeoRuleKind.Cidr:
                    cidrs.Add(rule.Value);
                    break;
                case GeoRuleKind.Domain:
                    domains.Add(new GeoDomain(GeoDomainKind.Domain, rule.Value));
                    break;
                case GeoRuleKind.GeoIp:
                case GeoRuleKind.GeoSite:
                    Expand(Key(rule.Value), index, cidrs, domains);
                    break;
            }
        }

        return new GeoTargets(cidrs, domains);
    }

    /// <summary>
    /// Expands one geo key into both of its facets: the ranges the databases give it and the names, the domain
    /// suffixes a country owns included.
    /// </summary>
    public static void Expand(string key, GeoIndex index, List<string> cidrs, List<GeoDomain> domains)
    {
        cidrs.AddRange(index.Cidrs(key));
        domains.AddRange(index.Domains(key));
        foreach (var suffix in CountryDomains.Suffixes(key))
        {
            domains.Add(new GeoDomain(GeoDomainKind.Domain, suffix));
        }
    }

    /// <summary>
    /// Takes the code out of a value written as geoip:ru or geosite:youtube.
    /// </summary>
    public static string Key(string value)
    {
        var colon = value.IndexOf(':', StringComparison.Ordinal);

        return colon >= 0 ? value[(colon + 1)..] : value;
    }
}
