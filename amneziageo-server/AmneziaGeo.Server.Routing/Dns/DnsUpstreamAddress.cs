using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// How a name server is asked.
/// </summary>
public enum DnsTransport
{
    Plain = 0,
    Tls = 1,
    Https = 2,
}

/// <summary>
/// A name server the questions are passed to, with the way it is asked.
/// </summary>
/// <param name="Transport">How the server is asked.</param>
/// <param name="Point">The address and the port of the server.</param>
/// <param name="Path">The path the questions go to over HTTPS, empty for the other ways.</param>
public sealed record DnsUpstreamAddress(DnsTransport Transport, IPEndPoint Point, string Path)
{
    /// <summary>
    /// What a name server asked over TLS starts with.
    /// </summary>
    public const string TlsScheme = "tls://";

    /// <summary>
    /// What a name server asked over HTTPS starts with.
    /// </summary>
    public const string HttpsScheme = "https://";

    /// <summary>
    /// The port a name server takes questions over TLS on.
    /// </summary>
    public const int TlsPort = 853;

    /// <summary>
    /// The port a name server takes questions over HTTPS on.
    /// </summary>
    public const int HttpsPort = 443;

    /// <summary>
    /// The path a name server takes questions over HTTPS under.
    /// </summary>
    public const string HttpsPath = "/dns-query";

    /// <summary>
    /// Tells whether the questions travel encrypted.
    /// </summary>
    public bool IsEncrypted => Transport != DnsTransport.Plain;

    /// <summary>
    /// Returns the address the questions go to over HTTPS.
    /// </summary>
    public Uri Url => new(HttpsScheme + Host() + Path);

    /// <summary>
    /// Returns the server the way the settings write it.
    /// </summary>
    public override string ToString() => Transport switch
    {
        DnsTransport.Tls => TlsScheme + Host(),
        DnsTransport.Https => HttpsScheme + Host() + Path,
        _ => Host(),
    };

    private string Host()
    {
        var address = Point.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{Point.Address}]" : Point.Address.ToString();

        return address + ":" + Point.Port.ToString(CultureInfo.InvariantCulture);
    }
}
