using System.Security.Cryptography;

namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// What a proxy runs under when nothing is set.
/// </summary>
public static class ProxyDefaults
{
    /// <summary>
    /// The port a proxy listens on.
    /// </summary>
    public const int Port = 443;

    /// <summary>
    /// Where the files of the proxies are written.
    /// </summary>
    public const string Directory = "/etc/amneziageo-server";

    /// <summary>
    /// The service a websocket proxy runs as, with the name of the proxy after the at sign.
    /// </summary>
    public const string Service = "amneziageo-proxy";

    /// <summary>
    /// The service a wireguard proxy runs as, with the name of the proxy after the at sign.
    /// </summary>
    public const string RelayService = "amneziageo-relay";

    /// <summary>
    /// The host a fresh wireguard proxy passes the datagrams on to.
    /// </summary>
    public const string Loopback = "127.0.0.1";

    /// <summary>
    /// How long a wireguard proxy holds a client with nothing coming through, in seconds.
    /// </summary>
    public const int Idle = 180;

    /// <summary>
    /// How many bytes the path of a fresh proxy carries.
    /// </summary>
    public const int PathBytes = 18;

    /// <summary>
    /// The name a fresh proxy takes.
    /// </summary>
    public const string FirstName = "proxy0";

    /// <summary>
    /// Returns a proxy of a kind, with a path of its own when the kind serves one.
    /// </summary>
    public static ProxyConfig Fresh(string name, string? kind = null)
    {
        var taken = ProxyKind.Known(kind) ? kind! : ProxyKind.Ws;

        return new ProxyConfig
        {
            Name = name,
            Kind = taken,
            Port = Port,
            Path = ProxyKind.HasPath(taken) ? Secret() : string.Empty,
        };
    }

    /// <summary>
    /// Returns a path no one guesses.
    /// </summary>
    public static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(PathBytes))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
