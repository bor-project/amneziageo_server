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
    /// The service a proxy runs as, with the name of the proxy after the at sign.
    /// </summary>
    public const string Service = "amneziageo-proxy";

    /// <summary>
    /// How many bytes the path of a fresh proxy carries.
    /// </summary>
    public const int PathBytes = 18;

    /// <summary>
    /// The name a fresh proxy takes.
    /// </summary>
    public const string FirstName = "proxy0";

    /// <summary>
    /// Returns a proxy with a path of its own.
    /// </summary>
    public static ProxyConfig Fresh(string name) => new()
    {
        Name = name,
        Port = Port,
        Path = Secret(),
    };

    /// <summary>
    /// Returns a path no one guesses.
    /// </summary>
    public static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(PathBytes))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
