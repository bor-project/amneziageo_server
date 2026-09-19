using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Api.Hello;

/// <summary>
/// What the server answers to a caller that has not proven anything yet.
/// </summary>
/// <param name="Server">The name the panel answers under, always the same word.</param>
/// <param name="Version">The version of the server.</param>
/// <param name="Challenge">What a client answers with the key of its configuration.</param>
public sealed record HelloResponse(string Server, string Version, string Challenge);

/// <summary>
/// The answer a client gives to a challenge.
/// </summary>
/// <param name="Key">The public key of the client, in base64.</param>
/// <param name="Challenge">The challenge the server handed out.</param>
/// <param name="Nonce">The bytes the countersign of the server is bound to, in base64.</param>
/// <param name="Proof">The answer counted from the private key of the client.</param>
public sealed record HelloRequest(string? Key, string? Challenge, string? Nonce, string? Proof);

/// <summary>
/// Arguments of the subscription feature.
/// </summary>
/// <param name="Url">The address the client reads its subscription from.</param>
/// <param name="UpdateHours">How often it reads it again.</param>
public sealed record SubscriptionFeature(string Url, int UpdateHours);

/// <summary>
/// Arguments of the speed feature.
/// </summary>
/// <param name="Down">The address the client pulls bytes from.</param>
/// <param name="Up">The address the client sends bytes to.</param>
/// <param name="Limit">The most bytes one leg carries.</param>
/// <param name="Expires">When the pass in the addresses stops answering.</param>
public sealed record SpeedFeature(string Down, string Up, long Limit, DateTimeOffset Expires);

/// <summary>
/// Arguments of the websocket feature.
/// </summary>
/// <param name="Host">The name the front answers under, empty for the host of the Endpoint.</param>
/// <param name="Port">The TCP port of the front.</param>
/// <param name="Path">The secret path the front serves the tunnel under.</param>
/// <param name="Target">The UDP port the front hands the tunnel to.</param>
public sealed record WebSocketFeature(string Host, int Port, string Path, int Target);

/// <summary>
/// What the server lets a client that proved its key know about itself.
/// </summary>
/// <param name="Server">The name the panel answers under.</param>
/// <param name="Version">The version of the server.</param>
/// <param name="Client">The name of the client behind the key.</param>
/// <param name="Features">The arguments of every feature offered to this client, by feature name.</param>
public sealed record FeatureResponse(
    string Server,
    string Version,
    string Client,
    IReadOnlyDictionary<string, object> Features);

/// <summary>
/// The client that proved its key and the request it asked with.
/// </summary>
/// <param name="Client">The client behind the key.</param>
/// <param name="Endpoint">The endpoint the client connects to.</param>
/// <param name="Context">The request of the client.</param>
public sealed record HelloPeer(TunnelClient Client, ServerConfig Endpoint, HttpContext Context);

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
    /// The word the panel answers under, so a client knows what it reached.
    /// </summary>
    public const string Server = "amneziageo";

    /// <summary>
    /// The subscriptions of the clients.
    /// </summary>
    public const string Subscription = "subscription";

    /// <summary>
    /// The measurement of the speed against the server.
    /// </summary>
    public const string Speed = "speed";

    /// <summary>
    /// The websocket front the tunnel is carried through.
    /// </summary>
    public const string WebSocket = "websocket";
}
