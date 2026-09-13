using System.Globalization;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Panel;

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

        if (!peer.Endpoint.Speed)
        {
            return ValueTask.FromResult<object?>(null);
        }

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
