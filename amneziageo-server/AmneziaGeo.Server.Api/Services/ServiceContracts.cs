using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// The token a client proves the keys of its configuration with.
/// </summary>
/// <param name="Key">The public key of the client, in base64.</param>
/// <param name="Time">When the token was counted, in seconds since 1970.</param>
/// <param name="Nonce">The random bytes of the token, in base64.</param>
/// <param name="Proof">The mark counted from the secret the client and the endpoint share, in base64.</param>
public sealed record HelloRequest(string? Key, long Time, string? Nonce, string? Proof);

/// <summary>
/// What the services refuse a token with.
/// </summary>
/// <param name="Error">The code of the refusal.</param>
/// <param name="Message">What went wrong, in words.</param>
/// <param name="Time">The clock of the server in seconds since 1970, named when the token ran out.</param>
public sealed record HelloFailure(string Error, string Message, long? Time = null);

/// <summary>
/// What the server lets a client that proved its key know about itself.
/// </summary>
/// <param name="Server">The name the server answers under.</param>
/// <param name="Version">The version of the server.</param>
/// <param name="Client">The name of the client behind the key.</param>
/// <param name="Features">The arguments of every feature offered to this client, by feature name.</param>
public sealed record FeatureResponse(
    string Server,
    string Version,
    string Client,
    IReadOnlyDictionary<string, object> Features);

/// <summary>
/// Arguments of the websocket feature.
/// </summary>
/// <param name="Port">The TCP port the websocket answers on.</param>
public sealed record WebSocketFeature(int Port);

/// <summary>
/// Arguments of the routing feature.
/// </summary>
/// <param name="Allowed">Whether the client routes by its own lists.</param>
public sealed record RoutingFeature(bool Allowed);

/// <summary>
/// Arguments of the inbound feature.
/// </summary>
/// <param name="Mode">What the client takes from the tunnel.</param>
public sealed record InboundFeature(string Mode);

/// <summary>
/// The addresses one leg of a measurement goes to.
/// </summary>
/// <param name="Down">The address the client pulls bytes from.</param>
/// <param name="Up">The address the client sends bytes to.</param>
public sealed record SpeedLeg(string Down, string Up);

/// <summary>
/// Arguments of the speed feature.
/// </summary>
/// <param name="Inside">The addresses inside the tunnel.</param>
/// <param name="Outside">The addresses outside the tunnel.</param>
/// <param name="Limit">The most bytes one leg carries.</param>
/// <param name="Expires">When the pass in the addresses stops answering.</param>
public sealed record SpeedFeature(SpeedLeg Inside, SpeedLeg Outside, long Limit, DateTimeOffset Expires);

/// <summary>
/// Arguments of the subscription feature.
/// </summary>
/// <param name="Url">The address the client reads its subscription at.</param>
/// <param name="Revision">The mark of what the subscription hands out now.</param>
/// <param name="Pin">The SHA-256 of the certificate the address answers under, empty when the address is elsewhere.</param>
public sealed record SubscriptionFeature(string Url, string Revision, string Pin);

/// <summary>
/// The client that proved its key and the request it asked with.
/// </summary>
/// <param name="Client">The client behind the key.</param>
/// <param name="Endpoint">The endpoint the client connects to.</param>
/// <param name="Template">The template of the client, or null for none.</param>
/// <param name="Context">The request of the client.</param>
public sealed record HelloPeer(TunnelClient Client, ServerConfig Endpoint, ClientTemplate? Template, HttpContext Context);

/// <summary>
/// One feature the server offers to a client that proved its key.
/// </summary>
public interface IHelloFeature
{
    /// <summary>
    /// The key of the feature in the dictionary.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns the arguments of the feature for a peer, or null when it is not offered.
    /// </summary>
    ValueTask<object?> OfferAsync(HelloPeer peer, CancellationToken ct);
}

/// <summary>
/// The names of what the server offers.
/// </summary>
public static class FeatureNames
{
    /// <summary>
    /// The word the server answers under, so a client knows what it reached.
    /// </summary>
    public const string Server = "amneziageo";

    /// <summary>
    /// The tunnel inside a websocket.
    /// </summary>
    public const string WebSocket = "websocket";

    /// <summary>
    /// The routing of the client by its own lists.
    /// </summary>
    public const string Routing = "routing";

    /// <summary>
    /// What the client takes from the tunnel.
    /// </summary>
    public const string Inbound = "inbound";

    /// <summary>
    /// The measurement of the speed against the server.
    /// </summary>
    public const string Speed = "speed";

    /// <summary>
    /// The subscription of the client.
    /// </summary>
    public const string Subscription = "subscription";
}
