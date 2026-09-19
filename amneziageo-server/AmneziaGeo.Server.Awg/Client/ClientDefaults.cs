using System.Security.Cryptography;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// The values a new client starts with.
/// </summary>
public static class ClientDefaults
{
    private const string Letters = "abcdefghijklmnopqrstuvwxyz0123456789";

    private const int SubscriptionLength = 16;

    /// <summary>
    /// Returns a client of an endpoint with a key pair and a subscription of its own.
    /// </summary>
    public static TunnelClient Fresh(long configId, string name)
    {
        var pair = Curve25519.Create();

        return new TunnelClient
        {
            ConfigId = configId,
            Name = name,
            PrivateKey = pair.PrivateKey,
            PublicKey = pair.PublicKey,
            SubscriptionId = SubscriptionId(),
            Inbound = ClientInbound.Endpoint,
        };
    }

    /// <summary>
    /// Returns the client with the keys it lacks, keeping the ones it carries.
    /// </summary>
    public static TunnelClient Keyed(TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (client.PublicKey.Length > 0)
        {
            return client;
        }

        if (client.PrivateKey.Length > 0)
        {
            return Curve25519.IsKey(client.PrivateKey)
                ? client with { PublicKey = Curve25519.PublicOf(client.PrivateKey) }
                : client;
        }

        var pair = Curve25519.Create();

        return client with { PrivateKey = pair.PrivateKey, PublicKey = pair.PublicKey };
    }

    /// <summary>
    /// Returns a name of a subscription no one guesses.
    /// </summary>
    public static string SubscriptionId() => RandomNumberGenerator.GetString(Letters, SubscriptionLength);
}
