using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// The values a new client starts with.
/// </summary>
public static class ClientDefaults
{
    /// <summary>
    /// Returns a client of an endpoint with a key pair of its own.
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
        };
    }
}
