using AmneziaGeo.Server.Routing.Probe;

namespace AmneziaGeo.Server.Routing.Outbound;

/// <summary>
/// What the host holds for one outbound.
/// </summary>
/// <param name="Name">The name of the outbound.</param>
/// <param name="HasLink">Whether the interface is on the host.</param>
/// <param name="Endpoint">The address the tunnel speaks to.</param>
/// <param name="LastHandshake">When the server last answered a handshake.</param>
/// <param name="RxBytes">Bytes taken from the server.</param>
/// <param name="TxBytes">Bytes given to the server.</param>
/// <param name="IsAlive">Whether the last handshake is recent enough to carry traffic.</param>
/// <param name="Fault">Why the host does not hold the outbound.</param>
/// <param name="Probe">What the probes of the outbound came to, or null when it was never probed.</param>
public sealed record OutboundState(
    string Name,
    bool HasLink,
    string Endpoint,
    DateTimeOffset? LastHandshake,
    ulong RxBytes,
    ulong TxBytes,
    bool IsAlive,
    string Fault,
    ProbeReading? Probe = null)
{
    /// <summary>
    /// Tells whether the outbound carries traffic, taking the probe over the handshake.
    /// </summary>
    public bool Carries => IsAlive && Probe is not { IsReached: false };

    /// <summary>
    /// How long a handshake stands before the tunnel counts as dead.
    /// </summary>
    public static readonly TimeSpan AliveFor = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Returns the state of an outbound the host does not carry.
    /// </summary>
    public static OutboundState Missing(string name, string fault = "") =>
        new(name, false, string.Empty, null, 0, 0, false, fault);
}
