using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Routing.Outbound;

/// <summary>
/// The values a new outbound starts with.
/// </summary>
public static class OutboundDefaults
{
    /// <summary>
    /// The name of the outbound that leaves through the uplink of the host.
    /// </summary>
    public const string DirectName = "direct";

    /// <summary>
    /// The packet size a new tunnel carries.
    /// </summary>
    public const int Mtu = 1420;

    /// <summary>
    /// How often a new tunnel holds the path to the server open, in seconds.
    /// </summary>
    public const int Keepalive = 25;

    /// <summary>
    /// The port a new tunnel goes to.
    /// </summary>
    public const int Port = 51820;

    /// <summary>
    /// Returns the outbound a fresh database is seeded with.
    /// </summary>
    public static OutboundConfig Direct() => new()
    {
        Name = DirectName,
        Kind = OutboundKind.Local,
        Position = 1,
        IsEnabled = true,
        Mark = OutboundRules.FirstMark,
        Table = OutboundRules.TableOf(OutboundRules.FirstMark, OutboundKind.Local),
    };

    /// <summary>
    /// Returns a tunnel with a key pair of its own.
    /// </summary>
    public static OutboundConfig Fresh(string name)
    {
        var pair = Curve25519.Create();

        return new OutboundConfig
        {
            Name = name,
            Kind = OutboundKind.Wg,
            Port = Port,
            Mtu = Mtu,
            Keepalive = Keepalive,
            PrivateKey = pair.PrivateKey,
            PublicKey = pair.PublicKey,
        };
    }
}
