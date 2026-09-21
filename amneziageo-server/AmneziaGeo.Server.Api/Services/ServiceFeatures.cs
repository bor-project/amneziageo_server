using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Offers the tunnel inside a websocket on the port of the services.
/// </summary>
public sealed class WebSocketOffer : IHelloFeature
{
    /// <inheritdoc/>
    public string Name => FeatureNames.WebSocket;

    /// <inheritdoc/>
    public object? Offer(HelloPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        return peer.Endpoint.WebSocket ? new WebSocketFeature(ConfigServices.Port(peer.Endpoint)) : null;
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
    public object? Offer(HelloPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        return new RoutingFeature(RoutingName.Taken(peer.Client.Routing, peer.Template));
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
    public object? Offer(HelloPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var inbound = InboundName.Taken(peer.Client.Inbound, peer.Endpoint.Inbound);

        return inbound == ClientInbound.Off ? null : new InboundFeature(InboundName.Of(inbound));
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
    public object? Offer(HelloPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        return peer.Client.Routes.Count > 0 ? new RoutesFeature([.. peer.Client.Routes]) : null;
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
    public object? Offer(HelloPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var ticket = _tickets.Mint(peer.Client.Id);
        var port = ConfigServices.Port(peer.Endpoint);
        var outside = peer.Endpoint.Host.Length > 0 ? peer.Endpoint.Host : peer.Context.Request.Host.Host;
        var inside = Inside(peer.Endpoint) ?? outside;

        return new SpeedFeature(Leg(inside, port, ticket), Leg(outside, port, ticket), SpeedTickets.MaxBytes, ticket.Expires);
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
