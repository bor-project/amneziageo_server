using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Proxy;

/// <summary>
/// Names the websocket front the configuration of a client carries its tunnel through.
/// </summary>
public sealed class WebSocketFronts
{
    private const string AltNames = "2.5.29.17";

    private readonly ProxyStore _proxies;

    private readonly ProxyApplier _applier;

    private readonly ILogger<WebSocketFronts> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public WebSocketFronts(ProxyStore proxies, ProxyApplier applier, ILogger<WebSocketFronts> logger)
    {
        _proxies = proxies;
        _applier = applier;
        _logger = logger;
    }

    /// <summary>
    /// Returns the address of the front for every endpoint, empty where none serves.
    /// </summary>
    public async Task<Func<ServerConfig, string>> ReadAsync(CancellationToken ct)
    {
        var proxies = await _proxies.ListAsync(ct).ConfigureAwait(false);
        var panel = await _applier.PanelCertificateAsync(ct).ConfigureAwait(false);

        return endpoint => Front(proxies, endpoint, panel, _logger);
    }

    /// <summary>
    /// Returns the address of the first websocket proxy that is turned on and answers under TLS, one open to every
    /// source ahead of the rest, empty when none is.
    /// </summary>
    public static string Front(IReadOnlyList<ProxyConfig> proxies, ServerConfig endpoint, ProxyCertificate panel, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(proxies);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(logger);

        foreach (var proxy in proxies.Where(Serves).OrderBy(proxy => proxy.Sources.Count > 0).ThenBy(proxy => proxy.Id))
        {
            var certificate = ProxyApplier.Certificate(proxy, panel);
            if (ProxyRules.CheckReady(proxy, certificate.Chain, certificate.Key) is not null)
            {
                continue;
            }

            var host = Host(endpoint.Host, certificate.Chain, logger);
            if (host.Length == 0)
            {
                continue;
            }

            return Address(host, proxy.Port, proxy.Path.Trim('/'));
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns the address a front goes by in the configuration.
    /// </summary>
    public static string Address(string host, int port, string path)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(path);

        var shown = IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{host}]"
            : host;

        return string.Create(CultureInfo.InvariantCulture, $"wss://{shown}:{port}/{path}");
    }

    // Tells whether a proxy serves the tunnel inside a websocket on a path of its own.
    private static bool Serves(ProxyConfig proxy) =>
        proxy.IsEnabled && ProxyKind.HasPath(proxy.Kind) && proxy.Path.Trim('/').Length > 0;

    // Returns the name the certificate answers under: the host of the endpoint when it holds it, else the first name it carries.
    private static string Host(string host, string chain, ILogger logger)
    {
        try
        {
            using var certificate = X509Certificate2.CreateFromPem(File.ReadAllText(chain));
            if (host.Length > 0 && certificate.MatchesHostname(host))
            {
                return host;
            }

            return Names(certificate).FirstOrDefault() ?? host;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            logger.LogWarning(ex, "the certificate of the websocket front does not read from {Path}", chain);

            return host;
        }
    }

    // Lists the names the certificate carries, without the wildcards.
    private static IEnumerable<string> Names(X509Certificate2 certificate) =>
        certificate.Extensions
            .Where(extension => string.Equals(extension.Oid?.Value, AltNames, StringComparison.Ordinal))
            .SelectMany(extension => new X509SubjectAlternativeNameExtension(extension.RawData).EnumerateDnsNames())
            .Where(name => !name.StartsWith("*.", StringComparison.Ordinal));
}
