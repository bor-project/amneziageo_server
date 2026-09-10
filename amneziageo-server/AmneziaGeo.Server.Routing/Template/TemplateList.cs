using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Template;

/// <summary>
/// Reads the entries a client template fills its ranges from.
/// </summary>
public static class TemplateList
{
    /// <summary>
    /// The most entries a template carries.
    /// </summary>
    public const int MaxEntries = 256;

    private static readonly string[] Marks = ["domain:", "cidr:"];

    /// <summary>
    /// Returns the entry written the way a template keeps it, or null when the text is none.
    /// </summary>
    public static string? Entry(string? text)
    {
        var body = Bare(text);
        if (RouteRules.Target(body) is not { } rule)
        {
            return null;
        }

        return rule.Kind switch
        {
            GeoRuleKind.GeoIp => "geoip:" + rule.Value.ToLowerInvariant(),
            GeoRuleKind.GeoSite => "geosite:" + rule.Value.ToLowerInvariant(),
            GeoRuleKind.Cidr => Range(body),
            _ => rule.Value,
        };
    }

    /// <summary>
    /// Returns why the entries of a template are unusable, or null when they hold.
    /// </summary>
    public static ClientFault? Check(IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count > MaxEntries)
        {
            return new ClientFault("too-many-entries", $"a template carries at most {MaxEntries} entries");
        }

        foreach (var entry in entries)
        {
            if (Entry(entry) is null)
            {
                return new ClientFault("bad-entry", $"'{entry}' is not a geo key, a network, an address or a domain");
            }
        }

        return null;
    }

    private static string Bare(string? text)
    {
        var body = (text ?? string.Empty).Trim();
        foreach (var mark in Marks)
        {
            if (body.StartsWith(mark, StringComparison.OrdinalIgnoreCase))
            {
                body = body[mark.Length..].Trim();
            }
        }

        var scheme = body.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
        {
            return body;
        }

        var host = body[(scheme + 3)..];
        var cut = host.IndexOfAny(['/', '?', '#', ':']);

        return cut < 0 ? host : host[..cut];
    }

    private static string Range(string body)
    {
        var range = AwgAllowedIp.Parse(body);
        var full = range.IsSix ? 128 : 32;

        return range.Cidr == full ? range.Address.ToString() : range.Network().ToString();
    }
}
