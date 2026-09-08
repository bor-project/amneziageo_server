using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Geo;

/// <summary>
/// Tells whether a name falls under a set of geosite entries.
/// </summary>
public sealed class DomainMatcher
{
    private static readonly TimeSpan MatchLimit = TimeSpan.FromMilliseconds(50);

    private readonly HashSet<string> _full = new(StringComparer.Ordinal);
    private readonly HashSet<string> _domain = new(StringComparer.Ordinal);
    private readonly List<string> _plain = [];
    private readonly List<Regex> _regex = [];

    /// <summary>
    /// ctor
    /// </summary>
    public DomainMatcher(IReadOnlyList<GeoDomain> domains)
    {
        foreach (var entry in domains)
        {
            switch (entry.Kind)
            {
                case GeoDomainKind.Full:
                    _full.Add(Normalize(entry.Value));
                    break;
                case GeoDomainKind.Domain:
                    _domain.Add(Normalize(entry.Value));
                    break;
                case GeoDomainKind.Plain:
                    _plain.Add(entry.Value.ToLowerInvariant());
                    break;
                case GeoDomainKind.Regex:
                    Add(entry.Value);
                    break;
            }
        }
    }

    /// <summary>
    /// Returns whether the name falls under any entry.
    /// </summary>
    public bool Matches(string domain) => Match(domain) is not null;

    /// <summary>
    /// Returns the entry the name fell under, or null.
    /// </summary>
    public GeoMatch? Match(string domain)
    {
        var host = domain.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0)
        {
            return null;
        }

        if (_full.Contains(host))
        {
            return new GeoMatch(GeoDomainKind.Full, host);
        }

        if (_domain.Contains(host))
        {
            return new GeoMatch(GeoDomainKind.Domain, host);
        }

        if (_domain.Count > 0)
        {
            for (var i = 0; i < host.Length; i++)
            {
                if (host[i] == '.' && _domain.Contains(host[(i + 1)..]))
                {
                    return new GeoMatch(GeoDomainKind.Domain, host[(i + 1)..]);
                }
            }
        }

        foreach (var value in _plain)
        {
            if (host.Contains(value, StringComparison.Ordinal))
            {
                return new GeoMatch(GeoDomainKind.Plain, value);
            }
        }

        foreach (var regex in _regex)
        {
            if (Hits(regex, host))
            {
                return new GeoMatch(GeoDomainKind.Regex, regex.ToString());
            }
        }

        return null;
    }

    /// <summary>
    /// The entry a name fell under.
    /// </summary>
    /// <param name="Kind">How the entry was matched.</param>
    /// <param name="Value">The entry itself.</param>
    public readonly record struct GeoMatch(GeoDomainKind Kind, string Value);

    // An expression the platform refuses is dropped, and the rest of the category still matches.
    private void Add(string pattern)
    {
        try
        {
            _regex.Add(new Regex(pattern, RegexOptions.None, MatchLimit));
        }
        catch (ArgumentException)
        {
        }
    }

    private static bool Hits(Regex regex, string host)
    {
        try
        {
            return regex.IsMatch(host);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string Normalize(string value) => value.TrimEnd('.').ToLowerInvariant();
}
