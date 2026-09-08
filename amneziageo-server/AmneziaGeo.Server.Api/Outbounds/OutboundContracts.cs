using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Api.Outbounds;

/// <summary>
/// What the host holds for an outbound, as the interface reads it.
/// </summary>
public sealed record OutboundStateBody(
    bool HasLink,
    string Endpoint,
    DateTimeOffset? LastHandshake,
    ulong RxBytes,
    ulong TxBytes,
    bool IsAlive,
    string Fault);

/// <summary>
/// An outbound as the interface reads it.
/// </summary>
public sealed record OutboundResponse(
    long Id,
    string Name,
    string Kind,
    int Position,
    bool IsEnabled,
    string Host,
    int Port,
    string PublicKey,
    string PeerKey,
    string? PrivateKey,
    string? PresharedKey,
    string[] Address,
    string[] Dns,
    int Mtu,
    int Keepalive,
    long Mark,
    int Table,
    ObfuscationBody Obfuscation,
    OutboundStateBody? State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// An outbound as the interface sends it.
/// </summary>
public sealed record OutboundRequest(
    string? Name,
    string? Kind,
    bool IsEnabled,
    string? Host,
    int Port,
    string? PrivateKey,
    string? PeerKey,
    string? PresharedKey,
    string[]? Address,
    string[]? Dns,
    int Mtu,
    int Keepalive,
    ObfuscationBody? Obfuscation);

/// <summary>
/// A client configuration the interface sends to be read into an outbound.
/// </summary>
public sealed record OutboundImportRequest(string? Name, string? Config);

/// <summary>
/// Where an outbound is moved to.
/// </summary>
public sealed record OutboundMoveRequest(bool Up);

/// <summary>
/// Whether an outbound is turned on.
/// </summary>
public sealed record OutboundSwitchRequest(bool On);

/// <summary>
/// Turns outbounds between the shape the panel holds and the shape the interface reads.
/// </summary>
public static class OutboundAnswers
{
    /// <summary>
    /// Describes an outbound as the interface reads it, with the keys only for a caller that changes it.
    /// </summary>
    public static OutboundResponse Outbound(OutboundConfig outbound, OutboundState? state, bool secrets)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        return new OutboundResponse(
            outbound.Id,
            outbound.Name,
            outbound.Kind,
            outbound.Position,
            outbound.IsEnabled,
            outbound.Host,
            outbound.Port,
            outbound.PublicKey,
            outbound.PeerKey,
            secrets ? outbound.PrivateKey : null,
            secrets ? outbound.PresharedKey : null,
            [.. outbound.Address],
            [.. outbound.Dns],
            outbound.Mtu,
            outbound.Keepalive,
            outbound.Mark,
            outbound.Table,
            ConfigAnswers.Obfuscation(outbound.Obfuscation),
            state is null ? null : State(state),
            outbound.CreatedUtc,
            outbound.UpdatedUtc);
    }

    /// <summary>
    /// Describes what the host holds for an outbound.
    /// </summary>
    public static OutboundStateBody State(OutboundState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new OutboundStateBody(
            state.HasLink,
            state.Endpoint,
            state.LastHandshake,
            state.RxBytes,
            state.TxBytes,
            state.IsAlive,
            state.Fault);
    }

    /// <summary>
    /// Reads the outbound an interface sends.
    /// </summary>
    public static OutboundConfig Draft(OutboundRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new OutboundConfig
        {
            Name = (request.Name ?? string.Empty).Trim(),
            Kind = (request.Kind ?? OutboundKind.Local).Trim(),
            IsEnabled = request.IsEnabled,
            Host = (request.Host ?? string.Empty).Trim(),
            Port = request.Port,
            PrivateKey = (request.PrivateKey ?? string.Empty).Trim(),
            PeerKey = (request.PeerKey ?? string.Empty).Trim(),
            PresharedKey = (request.PresharedKey ?? string.Empty).Trim(),
            Address = request.Address ?? [],
            Dns = request.Dns ?? [],
            Mtu = request.Mtu,
            Keepalive = request.Keepalive,
            Obfuscation = ConfigAnswers.Settings(request.Obfuscation),
        };
    }
}
