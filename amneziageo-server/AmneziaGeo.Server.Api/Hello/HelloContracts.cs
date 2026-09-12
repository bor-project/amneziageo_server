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
/// <param name="Proof">The answer counted from the private key of the client.</param>
public sealed record HelloRequest(string? Key, string? Challenge, string? Proof);

/// <summary>
/// Where the subscriptions of the client are served.
/// </summary>
/// <param name="Url">The address the client reads its subscription from.</param>
/// <param name="UpdateHours">How often it reads it again.</param>
public sealed record SubscriptionFeature(string Url, int UpdateHours);

/// <summary>
/// Where the client measures its speed against the server.
/// </summary>
/// <param name="Down">The address the client pulls bytes from.</param>
/// <param name="Up">The address the client sends bytes to.</param>
/// <param name="Limit">The most bytes one leg carries.</param>
/// <param name="Expires">When the pass in the addresses stops answering.</param>
public sealed record SpeedFeature(string Down, string Up, long Limit, DateTimeOffset Expires);

/// <summary>
/// What the server lets a client that proved its key know about itself.
/// </summary>
/// <param name="Server">The name the panel answers under.</param>
/// <param name="Version">The version of the server.</param>
/// <param name="Client">The name of the client behind the key.</param>
/// <param name="Features">What the server offers this client.</param>
/// <param name="Subscription">Where the subscription is served, null when it is not offered.</param>
/// <param name="Speed">Where the speed is measured, null when it is not offered.</param>
public sealed record FeatureResponse(
    string Server,
    string Version,
    string Client,
    IReadOnlyList<string> Features,
    SubscriptionFeature? Subscription,
    SpeedFeature? Speed);

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
}
