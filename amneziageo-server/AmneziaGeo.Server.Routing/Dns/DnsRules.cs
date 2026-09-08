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

        if (settings.Upstreams.FirstOrDefault(one => !Upstream(one, out _)) is { } wrong)
        {
            return new DnsFault("bad-upstream", $"'{wrong}' is not an address of a name server");
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
    /// Reads the address of a name server, with the port when it carries one.
    /// </summary>
    public static bool Upstream(string text, out IPEndPoint point)
    {
        point = new IPEndPoint(IPAddress.None, DnsDefaults.Port);
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return false;
        }

        var port = DnsDefaults.Port;
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

    /// <summary>
    /// Reads a bare address.
    /// </summary>
    public static bool Address(string text, out IPAddress address)
    {
        address = IPAddress.None;
        var value = (text ?? string.Empty).Trim();

        return value.Length > 0 && IPAddress.TryParse(value, out address!);
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
