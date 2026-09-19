using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Proxy;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Hello;

/// <summary>
/// Offers the address of the subscription of a client.
/// </summary>
public sealed class SubscriptionOffer : IHelloFeature
{
    private readonly SubscriptionState _subscriptions;

    private readonly PanelSettings _panel;

    private readonly WebOptions _options;

    /// <summary>
    /// ctor
    /// </summary>
    public SubscriptionOffer(SubscriptionState subscriptions, PanelSettings panel, WebOptions options)
    {
        _subscriptions = subscriptions;
        _panel = panel;
        _options = options;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.Subscription;

    /// <inheritdoc/>
    public ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var address = SubscriptionAnswer.Address(
            _subscriptions.Current,
            _panel,
            Listening.Chain(_options, _panel).Length > 0,
            peer.Endpoint.Host.Length > 0 ? peer.Endpoint.Host : peer.Context.Request.Host.Host,
            peer.Client.PrivateKey.Length > 0 ? peer.Client.SubscriptionId : string.Empty);

        return ValueTask.FromResult<object?>(address.Length == 0
            ? null
            : new SubscriptionFeature(address, _subscriptions.Current.UpdateHours));
    }
}

/// <summary>
/// Offers the addresses a client measures its speed against, with a fresh pass.
/// </summary>
public sealed class SpeedOffer : IHelloFeature
{
    private readonly SpeedTickets _tickets;

    /// <summary>
    /// ctor
    /// </summary>
    public SpeedOffer(SpeedTickets tickets)
    {
        _tickets = tickets;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.Speed;

    /// <inheritdoc/>
    public ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var ticket = _tickets.Mint(peer.Client.Id);
        var request = peer.Context.Request;
        var root = request.Scheme + "://" + request.Host.Value + HelloDesk.SpeedPath;
        var bytes = SpeedTickets.DefaultBytes.ToString(CultureInfo.InvariantCulture);

        return ValueTask.FromResult<object?>(new SpeedFeature(
            root + "/down?bytes=" + bytes + "&ticket=" + ticket.Value,
            root + "/up?ticket=" + ticket.Value,
            SpeedTickets.MaxBytes,
            ticket.Expires));
    }
}

/// <summary>
/// Offers the websocket front a client carries its tunnel through.
/// </summary>
public sealed class WebSocketOffer : IHelloFeature
{
    private const string AltNames = "2.5.29.17";

    private readonly IServiceScopeFactory _scopes;

    private readonly ILogger<WebSocketOffer> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public WebSocketOffer(IServiceScopeFactory scopes, ILogger<WebSocketOffer> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.WebSocket;

    /// <inheritdoc/>
    public async ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        using var scope = _scopes.CreateScope();
        var proxies = await scope.ServiceProvider.GetRequiredService<ProxyStore>().ListAsync(ct).ConfigureAwait(false);
        var panel = await scope.ServiceProvider.GetRequiredService<ProxyApplier>().PanelCertificateAsync(ct).ConfigureAwait(false);

        return Front(proxies, peer.Endpoint, panel, _logger);
    }

    /// <summary>
    /// Returns the front of the first websocket proxy that is turned on and answers under TLS, one open to every
    /// source ahead of the rest.
    /// </summary>
    public static WebSocketFeature? Front(IReadOnlyList<ProxyConfig> proxies, ServerConfig endpoint, ProxyCertificate panel, ILogger logger)
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

            return new WebSocketFeature(
                Host(endpoint.Host, certificate.Chain, logger),
                proxy.Port,
                proxy.Path.Trim('/'),
                endpoint.ListenPort);
        }

        return null;
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
