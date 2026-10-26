using System.Globalization;
using System.Security.Cryptography;
using AmneziaGeo.Server.Api.ClientUpdates;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Template;
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

        return ValueTask.FromResult<object?>(
            peer.Endpoint.WebSocket
                ? new WebSocketFeature(ConfigServices.Port(peer.Endpoint), ConfigServices.WebSocketPath(peer.Endpoint))
                : null);
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
/// Tells the client where it takes the signal to disconnect and where the signal comes from, unless the panel keeps
/// the client from the signal.
/// </summary>
public sealed class DisconnectOffer : IHelloFeature
{
    /// <inheritdoc/>
    public string Name => FeatureNames.Disconnect;

    /// <inheritdoc/>
    public ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var from = DisconnectSignal.Sources(peer.Endpoint);

        return ValueTask.FromResult<object?>(
            !peer.Client.Disconnect || from.Count == 0 || DisconnectSignal.Target(peer.Client) is null
                ? null
                : new DisconnectFeature(DisconnectSignal.Port, from, true));
    }
}

/// <summary>
/// Offers the addresses a client measures its speed against, with a fresh pass.
/// </summary>
public sealed class SpeedOffer : IHelloFeature
{
    private readonly SpeedTickets _tickets;

    private readonly ServiceShare? _share;

    /// <summary>
    /// ctor
    /// </summary>
    public SpeedOffer(SpeedTickets tickets, ServiceShare? share = null)
    {
        _tickets = tickets;
        _share = share;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.Speed;

    /// <inheritdoc/>
    public ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var ticket = _tickets.Mint(peer.Client.Id);
        var port = ConfigServices.Port(peer.Endpoint);
        var outside = PlainInside.Root("https", peer.Endpoint.Host.Length > 0 ? peer.Endpoint.Host : peer.Context.Request.Host.Host, port);
        var inside = PlainInside.Origin(peer.Endpoint, port, _share)
            ?? (PlainInside.Address(peer.Endpoint) is { } address ? PlainInside.Root("https", address.ToString(), port) : outside);

        return ValueTask.FromResult<object?>(
            new SpeedFeature(Leg(inside, ticket), Leg(outside, ticket), SpeedTickets.MaxBytes, ticket.Expires));
    }

    private static SpeedLeg Leg(string origin, SpeedTicket ticket)
    {
        var root = origin + ServiceDesk.SpeedPath;
        var bytes = SpeedTickets.DefaultBytes.ToString(CultureInfo.InvariantCulture);

        return new SpeedLeg(root + "/down?bytes=" + bytes + "&ticket=" + ticket.Value, root + "/up?ticket=" + ticket.Value);
    }
}

/// <summary>
/// Offers where the hello answers plain inside the tunnel.
/// </summary>
public sealed class InsideOffer : IHelloFeature
{
    private readonly ServiceShare? _share;

    /// <summary>
    /// ctor
    /// </summary>
    public InsideOffer(ServiceShare? share = null)
    {
        _share = share;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.Inside;

    /// <inheritdoc/>
    public ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var origin = PlainInside.Origin(peer.Endpoint, ConfigServices.Port(peer.Endpoint), _share);

        return ValueTask.FromResult<object?>(origin is null ? null : new InsideFeature(origin));
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

    private readonly SubscriptionCertificates? _certificates;

    /// <summary>
    /// ctor
    /// </summary>
    public SubscriptionOffer(
        SubscriptionState state,
        PanelSettings panel,
        WebOptions options,
        IServiceScopeFactory scopes,
        SubscriptionCertificates? certificates = null)
    {
        _state = state;
        _panel = panel;
        _options = options;
        _scopes = scopes;
        _certificates = certificates;
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

        var revision = SubscriptionAnswer.Revision(feed.Body);
        if (_certificates?.Of(url) is { IsGood: false } verdict)
        {
            return new SubscriptionFeature(url, revision, string.Empty, verdict.Fault, verdict.Detail);
        }

        return new SubscriptionFeature(url, revision, settings.Separate ? string.Empty : Pin(peer.Context));
    }

    // Returns the SHA-256 of the certificate the port answered under, empty without one.
    private static string Pin(HttpContext context) =>
        context.Features.Get<ISslStreamFeature>()?.SslStream.LocalCertificate is { } certificate
            ? Convert.ToHexStringLower(SHA256.HashData(certificate.GetRawCertData()))
            : string.Empty;
}

/// <summary>
/// Hands the client the geo sources the server reads, so the lists it routes by name the same keys.
/// </summary>
public sealed class SourcesOffer : IHelloFeature
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>
    /// ctor
    /// </summary>
    public SourcesOffer(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.Sources;

    /// <inheritdoc/>
    public async ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        using var scope = _scopes.CreateScope();
        var sources = await scope.ServiceProvider.GetRequiredService<GeoStore>().ListAsync(ct).ConfigureAwait(false);
        var items = sources.Where(source => source.IsEnabled)
            .OrderBy(source => source.Position)
            .Select(source => new SourceItem(source.Name, source.Kind, source.Url))
            .ToList();

        return items.Count == 0 ? null : new SourcesFeature(items);
    }
}

/// <summary>
/// Hands the client the routing lists its template names.
/// </summary>
public sealed class PresetsOffer : IHelloFeature
{
    private readonly SubscriptionState _state;

    private readonly PanelSettings _panel;

    private readonly WebOptions _options;

    private readonly IServiceScopeFactory _scopes;

    /// <summary>
    /// ctor
    /// </summary>
    public PresetsOffer(SubscriptionState state, PanelSettings panel, WebOptions options, IServiceScopeFactory scopes)
    {
        _state = state;
        _panel = panel;
        _options = options;
        _scopes = scopes;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.Presets;

    /// <inheritdoc/>
    public async ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        if (peer.Template is not { Presets.Count: > 0 } template)
        {
            return null;
        }

        using var scope = _scopes.CreateScope();
        var presets = await scope.ServiceProvider.GetRequiredService<PresetStore>()
            .ListAsync(template.Presets, ct)
            .ConfigureAwait(false);
        if (presets.Count == 0)
        {
            return null;
        }

        var naming = (await scope.ServiceProvider.GetRequiredService<PanelStore>().ReadAsync(ct).ConfigureAwait(false)).NameTemplate;
        var source = ClientText.Title(peer.Endpoint, peer.Client, naming, Host(peer));
        var lists = presets.Select(preset => new PresetItem(
                preset.Name,
                PresetRules.Rules(preset),
                preset.AllUdp,
                preset.Full,
                preset.Uid,
                preset.UpdatedUtc,
                preset.IsDefault,
                source,
                preset.ReadOnly))
            .ToList();

        return new PresetsFeature(lists, template.PresetsOnly);
    }

    // Returns the host the subscription names the configurations with, the one the client asked at without a subscription.
    private string Host(HelloPeer peer)
    {
        var asked = peer.Context.Request.Host.Host;
        var url = SubscriptionAnswer.Address(
            _state.Current,
            _panel,
            Listening.Chain(_options, _panel).Length > 0,
            asked,
            peer.Endpoint,
            peer.Client.SubscriptionId);

        return Uri.TryCreate(url, UriKind.Absolute, out var address) ? address.Host : asked;
    }
}

/// <summary>
/// Tells the client the name servers its file names, what picked them and where the resolver of the panel answers it.
/// </summary>
public sealed class DnsOffer : IHelloFeature
{
    private readonly DnsState _state;

    private readonly IServiceScopeFactory _scopes;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsOffer(DnsState state, IServiceScopeFactory scopes)
    {
        _state = state;
        _scopes = scopes;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.Dns;

    /// <inheritdoc/>
    public async ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        using var scope = _scopes.CreateScope();
        var held = await scope.ServiceProvider.GetRequiredService<DnsStore>().ReadAsync(ct).ConfigureAwait(false);
        var names = DnsHandout.For(peer.Endpoint, _state.Handing ?? held with { IsEnabled = false }, _state.KeptOut);
        var offered = DnsHandout.For(peer.Endpoint, _state.Chosen ?? held, _state.KeptOut);
        var line = ClientText.Dns(peer.Endpoint, peer.Client, peer.Template, names, offered, held.Files);
        var mode = line.From == DnsFrom.StandIns ? DnsChoiceName.Panel : DnsChoiceName.Of(line.Choice);

        return new DnsFeature(mode, line.Servers, _state.IsRunning ? offered : []);
    }
}

/// <summary>
/// Tells the client where it reads the releases of its own application, while the panel offers them.
/// </summary>
public sealed class UpdatesOffer : IHelloFeature
{
    private readonly ClientUpdateState _state;

    private readonly PanelSettings _panel;

    /// <summary>
    /// ctor
    /// </summary>
    public UpdatesOffer(ClientUpdateState state, PanelSettings panel)
    {
        _state = state;
        _panel = panel;
    }

    /// <inheritdoc/>
    public string Name => FeatureNames.Updates;

    /// <inheritdoc/>
    public ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var settings = _state.Current;
        if (!settings.IsEnabled)
        {
            return ValueTask.FromResult<object?>(null);
        }

        if (!settings.Own)
        {
            var url = ClientUpdateAnswers.Url(settings, _panel, peer.Context.Request.Host.Host);

            return ValueTask.FromResult<object?>(url.Length == 0 ? null : new UpdatesFeature(url, settings.Default, _state.Pin));
        }

        if (_state.Store is null)
        {
            return ValueTask.FromResult<object?>(null);
        }

        return ValueTask.FromResult<object?>(
            new UpdatesFeature(ClientUpdateAnswers.Url(settings, _panel, peer.Context.Request.Host.Host), settings.Default, Pin(peer.Context)));
    }

    // Returns the SHA-256 of the certificate the port answered under, empty without one.
    private static string Pin(HttpContext context) =>
        context.Features.Get<ISslStreamFeature>()?.SslStream.LocalCertificate is { } certificate
            ? Convert.ToHexStringLower(SHA256.HashData(certificate.GetRawCertData()))
            : string.Empty;
}
