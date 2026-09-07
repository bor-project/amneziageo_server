using System.Security.Cryptography;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// The values a new endpoint starts with.
/// </summary>
public static class ConfigDefaults
{
    /// <summary>
    /// The port a new endpoint takes packets on.
    /// </summary>
    public const int ListenPort = 51820;

    /// <summary>
    /// The packet size a new endpoint gives clients.
    /// </summary>
    public const int Mtu = 1420;

    /// <summary>
    /// How often a client of a new endpoint holds the path open, in seconds.
    /// </summary>
    public const int Keepalive = 25;

    /// <summary>
    /// The range a new endpoint carries on its interface.
    /// </summary>
    public const string Subnet = "10.8.0.1/24";

    /// <summary>
    /// The name servers a new endpoint gives clients.
    /// </summary>
    public static readonly string[] Dns = ["1.1.1.1", "1.0.0.1"];

    /// <summary>
    /// The ranges a client of a new endpoint routes into the tunnel.
    /// </summary>
    public static readonly string[] AllowedIps = ["0.0.0.0/0", "::/0"];

    /// <summary>
    /// Returns an endpoint with a key pair and obfuscation of its own.
    /// </summary>
    public static ServerConfig Fresh(string name)
    {
        var pair = Curve25519.Create();

        return new ServerConfig
        {
            Name = name,
            ListenPort = ListenPort,
            Address = [Subnet],
            Dns = [.. Dns],
            AllowedIps = [.. AllowedIps],
            Mtu = Mtu,
            Keepalive = Keepalive,
            PrivateKey = pair.PrivateKey,
            PublicKey = pair.PublicKey,
            Obfuscation = Obfuscation(),
        };
    }

    /// <summary>
    /// Returns obfuscation another endpoint is unlikely to repeat.
    /// </summary>
    public static ObfuscationSettings Obfuscation()
    {
        var first = RandomNumberGenerator.GetInt32(15, 150);
        var types = Types();

        return new ObfuscationSettings
        {
            Jc = RandomNumberGenerator.GetInt32(3, 11),
            Jmin = 50,
            Jmax = 1000,
            S1 = first,
            S2 = Second(first),
            H1 = types[0],
            H2 = types[1],
            H3 = types[2],
            H4 = types[3],
        };
    }

    private static int Second(int first)
    {
        var second = RandomNumberGenerator.GetInt32(15, 150);

        return second == first + ConfigRules.HandshakeGap ? second + 1 : second;
    }

    private static long[] Types()
    {
        var types = new List<long>(4);
        while (types.Count < 4)
        {
            var type = (long)RandomNumberGenerator.GetInt32(ConfigRules.LowestType, ConfigRules.HighestType);
            if (!types.Contains(type))
            {
                types.Add(type);
            }
        }

        return [.. types];
    }
}
