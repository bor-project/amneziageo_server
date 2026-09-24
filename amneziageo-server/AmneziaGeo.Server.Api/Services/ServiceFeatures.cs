using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Offers the tunnel inside a websocket on the port of the services.
/// </summary>
public sealed class WebSocketOffer : IHelloFeature
{
    /// <inheritdoc/>
    public string Name => FeatureNames.WebSocket;

    /// <inheritdoc/>
    public ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        return ValueTask.FromResult<object?>(peer.Endpoint.WebSocket ? new WebSocketFeature(ConfigServices.Port(peer.Endpoint)) : null);
    }
}

/// <summary>
/// Tells the client whether it routes by its own lists.
/// </summary>
public sealed class RoutingOffer : IHelloFeature
{
    /// <inheritdoc/>
    public string Name => FeatureNames.Routing;

    /// <inheritdoc/>
    public ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        return ValueTask.FromResult<object?>(new RoutingFeature(RoutingName.Taken(peer.Client.Routing, peer.Template)));
    }
}

/// <summary>
/// Tells the client what it takes from the tunnel.
/// </summary>
public sealed class InboundOffer : IHelloFeature
{
    /// <inheritdoc/>
    public string Name => FeatureNames.Inbound;

    /// <inheritdoc/>
    public ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var inbound = InboundName.Taken(peer.Client.Inbound, peer.Endpoint.Inbound);

        return ValueTask.FromResult<object?>(inbound == ClientInbound.Off ? null : new InboundFeature(InboundName.Of(inbound)));
    }
}

/// <summary>
/// Tells the client the ranges behind it.
/// </summary>
public sealed class RoutesOffer : IHelloFeature
{
    /// <inheritdoc/>
    public string Name => FeatureNames.Routes;

    /// <inheritdoc/>
    public ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        return ValueTask.FromResult<object?>(peer.Client.Routes.Count > 0 ? new RoutesFeature([.. peer.Client.Routes]) : null);
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
        var port = ConfigServices.Port(peer.Endpoint);
        var outside = peer.Endpoint.Host.Length > 0 ? peer.Endpoint.Host : peer.Context.Request.Host.Host;
        var inside = Inside(peer.Endpoint) ?? outside;

        return ValueTask.FromResult<object?>(
            new SpeedFeature(Leg(inside, port, ticket), Leg(outside, port, ticket), SpeedTickets.MaxBytes, ticket.Expires));
    }

    // Returns the address of the interface the tunnel reaches, the first one of IPv4 ahead of the rest.
    private static string? Inside(ServerConfig endpoint)
    {
        var addresses = endpoint.Address
            .Select(range => IPAddress.TryParse(range.Split('/')[0].Trim(), out var found) ? found : null)
            .OfType<IPAddress>()
            .ToList();
        var chosen = addresses.FirstOrDefault(one => one.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();

        return chosen?.ToString();
    }

    private static SpeedLeg Leg(string host, int port, SpeedTicket ticket)
    {
        var shown = IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{host}]"
            : host;
        var root = string.Create(CultureInfo.InvariantCulture, $"https://{shown}:{port}{ServiceDesk.SpeedPath}");
        var bytes = SpeedTickets.DefaultBytes.ToString(CultureInfo.InvariantCulture);

        return new SpeedLeg(root + "/down?bytes=" + bytes + "&ticket=" + ticket.Value, root + "/up?ticket=" + ticket.Value);
    }
}

/// <summary>
/// Tells the client where it reads its subscription and what the subscription hands out now.
/// </summary>
public sealed class SubscriptionOffer : IHelloFeature
{
    private readonly SubscriptionState _state;

    private readonly PanelSettings _panel;

    private readonly WebOptions _options;

    private readonly IServiceScopeFactory _scopes;

    /// <summary>
    /// ctor
    /// </summary>
    public SubscriptionOffer(SubscriptionState state, PanelSettings panel, WebOptions options, IServiceScopeFactory scopes)
    {
        _state = state;
        _panel = panel;
        _options = options;
        _scopes = scopes;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.Subscription;

    /// <inheritdoc/>
    public async ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var settings = _state.Current;
        var client = peer.Client;
        if (!settings.IsEnabled || client.SubscriptionId.Length == 0 || client.PrivateKey.Length == 0)
        {
            return null;
        }

        var url = SubscriptionAnswer.Address(
            settings,
            _panel,
            Listening.Chain(_options, _panel).Length > 0,
            peer.Context.Request.Host.Host,
            peer.Endpoint,
            client.SubscriptionId);

        // Reads the subscription with the host its address names, as the client reads it.
        var host = Uri.TryCreate(url, UriKind.Absolute, out var address) ? address.Host : peer.Context.Request.Host.Host;
        using var scope = _scopes.CreateScope();
        var feed = await scope.ServiceProvider.GetRequiredService<SubscriptionFeed>()
            .ReadAsync(client.SubscriptionId, host, ct)
            .ConfigureAwait(false);
        if (feed is null)
        {
            return null;
        }

        return new SubscriptionFeature(url, SubscriptionAnswer.Revision(feed.Body), settings.Separate ? string.Empty : Pin(peer.Context));
    }

    // Returns the SHA-256 of the certificate the port answered under, empty without one.
    private static string Pin(HttpContext context) =>
        context.Features.Get<ISslStreamFeature>()?.SslStream.LocalCertificate is { } certificate
            ? Convert.ToHexStringLower(SHA256.HashData(certificate.GetRawCertData()))
            : string.Empty;
}
