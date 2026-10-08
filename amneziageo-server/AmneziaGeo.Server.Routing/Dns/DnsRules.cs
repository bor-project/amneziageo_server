using System.Globalization;
using System.Net;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// What the resolver settings are allowed to carry.
/// </summary>
public static class DnsRules
{
    /// <summary>
    /// How many name servers the questions may be passed to.
    /// </summary>
    public const int MaxUpstreams = 8;

    /// <summary>
    /// How many addresses the resolver may be told to listen on.
    /// </summary>
    public const int MaxListen = 16;

    /// <summary>
    /// The longest an answered address may stay in the set of a rule, in minutes.
    /// </summary>
    public const int MaxNameMinutes = 1440;

    /// <summary>
    /// How many answers may be held back.
    /// </summary>
    public const int MaxCacheSize = 100000;

    /// <summary>
    /// The longest an answer may be held back, in seconds.
    /// </summary>
    public const int MaxLifetime = 604800;

    /// <summary>
    /// The longest path a name server may be asked under over HTTPS.
    /// </summary>
    public const int MaxPath = 128;

    /// <summary>
    /// Returns what is wrong with the settings, or null when they are good.
    /// </summary>
    public static DnsFault? Check(DnsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Port is < 1 or > 65535)
        {
            return new DnsFault("bad-port", "the port of the resolver is outside 1 to 65535");
        }

        if (settings.Upstreams.Count == 0)
        {
            return new DnsFault("no-upstream", "the resolver has no name server to pass the questions to");
        }

        if (settings.Upstreams.Count > MaxUpstreams)
        {
            return new DnsFault("no-upstream", $"the resolver takes at most {MaxUpstreams} name servers");
        }

        if (settings.Upstreams.FirstOrDefault(one => !Server(one, out _)) is { } wrong)
        {
            return new DnsFault(
                "bad-upstream",
                $"'{wrong}' is not a name server: an address, tls://address or https://address/path, with a port when it is not the usual one");
        }

        if (settings.Listen.Count > MaxListen)
        {
            return new DnsFault("bad-listen", $"the resolver listens on at most {MaxListen} addresses");
        }

        if (settings.Listen.FirstOrDefault(one => !Address(one, out _)) is { } bad)
        {
            return new DnsFault("bad-listen", $"'{bad}' is not an address to listen on");
        }

        return Numbers(settings);
    }

    /// <summary>
    /// Reads a name server with the way it is asked: a bare address for plain DNS, tls:// for DNS over TLS and
    /// https:// for DNS over HTTPS.
    /// </summary>
    public static bool Server(string text, out DnsUpstreamAddress server)
    {
        var read = Read((text ?? string.Empty).Trim());
        server = read ?? new DnsUpstreamAddress(DnsTransport.Plain, new IPEndPoint(IPAddress.None, DnsDefaults.Port), string.Empty);

        return read is not null;
    }

    /// <summary>
    /// Reads the address of a name server, with the port when it carries one.
    /// </summary>
    public static bool Upstream(string text, out IPEndPoint point) => Point(text, DnsDefaults.Port, out point);

    /// <summary>
    /// Reads a bare address.
    /// </summary>
    public static bool Address(string text, out IPAddress address)
    {
        address = IPAddress.None;
        var value = (text ?? string.Empty).Trim();

        return value.Length > 0 && IPAddress.TryParse(value, out address!);
    }

    private static DnsUpstreamAddress? Read(string value)
    {
        if (value.StartsWith(DnsUpstreamAddress.TlsScheme, StringComparison.OrdinalIgnoreCase))
        {
            return Point(value[DnsUpstreamAddress.TlsScheme.Length..], DnsUpstreamAddress.TlsPort, out var secure)
                ? new DnsUpstreamAddress(DnsTransport.Tls, secure, string.Empty)
                : null;
        }

        if (value.StartsWith(DnsUpstreamAddress.HttpsScheme, StringComparison.OrdinalIgnoreCase))
        {
            var rest = value[DnsUpstreamAddress.HttpsScheme.Length..];
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            var path = slash < 0 || slash == rest.Length - 1 ? DnsUpstreamAddress.HttpsPath : rest[slash..];

            return Point(slash < 0 ? rest : rest[..slash], DnsUpstreamAddress.HttpsPort, out var web) && Path(path)
                ? new DnsUpstreamAddress(DnsTransport.Https, web, path)
                : null;
        }

        return Point(value, DnsDefaults.Port, out var plain)
            ? new DnsUpstreamAddress(DnsTransport.Plain, plain, string.Empty)
            : null;
    }

    private static bool Path(string path) =>
        path.Length <= MaxPath && path.All(letter => char.IsAsciiLetterOrDigit(letter) || "/-._~%".Contains(letter, StringComparison.Ordinal));

    private static bool Point(string text, int usual, out IPEndPoint point)
    {
        point = new IPEndPoint(IPAddress.None, usual);
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return false;
        }

        var port = usual;
        var host = value;
        if (value[0] == '[')
        {
            var close = value.IndexOf(']', StringComparison.Ordinal);
            if (close < 0 || !Tail(value[(close + 1)..], ref port))
            {
                return false;
            }

            host = value[1..close];
        }
        else if (value.Count(letter => letter == ':') == 1)
        {
            var colon = value.IndexOf(':', StringComparison.Ordinal);
            if (!Tail(value[colon..], ref port))
            {
                return false;
            }

            host = value[..colon];
        }

        if (!Address(host, out var address))
        {
            return false;
        }

        point = new IPEndPoint(address, port);

        return true;
    }

    private static DnsFault? Numbers(DnsSettings settings)
    {
        if (settings.NameMinutes is < 1 || settings.NameMinutes > MaxNameMinutes)
        {
            return new DnsFault("bad-lifetime", $"the addresses live from 1 to {MaxNameMinutes} minutes");
        }

        if (settings.CacheSize is < 0 || settings.CacheSize > MaxCacheSize)
        {
            return new DnsFault("bad-cache", $"the resolver holds back from 0 to {MaxCacheSize} answers");
        }

        if (settings.MinTtl is < 0 || settings.MaxTtl > MaxLifetime || settings.MinTtl > settings.MaxTtl)
        {
            return new DnsFault("bad-ttl", $"an answer is held back from 0 to {MaxLifetime} seconds");
        }

        return null;
    }

    private static bool Tail(string text, ref int port)
    {
        if (text.Length == 0)
        {
            return true;
        }

        if (text[0] != ':' || !int.TryParse(text[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        port = value;

        return value is >= 1 and <= 65535;
    }
}
