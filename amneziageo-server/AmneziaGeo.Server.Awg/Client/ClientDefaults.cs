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
        };
    }

    /// <summary>
    /// Returns a name of a subscription no one guesses.
    /// </summary>
    public static string SubscriptionId() => RandomNumberGenerator.GetString(Letters, SubscriptionLength);
}
