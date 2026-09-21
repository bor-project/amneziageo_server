using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// The obfuscation of an endpoint as the interface reads and sends it.
/// </summary>
public sealed record ObfuscationBody(
    int Jc,
    int Jmin,
    int Jmax,
    int S1,
    int S2,
    int S3,
    int S4,
    string? H1,
    string? H2,
    string? H3,
    string? H4,
    string? I1,
    string? I2,
    string? I3,
    string? I4,
    string? I5,
    string? HeaderProtectionKey,
    string? ContentPaddingAddition,
    string? RekeyAfterTime,
    string? RekeyTimeout,
    string? RejectAfterTime,
    string? KeepaliveTimeout,
    string? MaxHandshakeAttempts,
    bool RandomTrailers,
    bool DisableCookies);

/// <summary>
/// An endpoint as the interface reads it.
/// </summary>
public sealed record ConfigResponse(
    long Id,
    long? TemplateId,
    string Name,
    string Host,
    int ListenPort,
    string[] Address,
    string[] Dns,
    string[] AllowedIps,
    int Mtu,
    int Keepalive,
    int OfflineAfter,
    bool IsEnabled,
    bool Nat,
    bool Opened,
    string Inbound,
    string[] Blocked,
    string PublicKey,
    string? PrivateKey,
    string? PresharedKey,
    ObfuscationBody Obfuscation,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// An endpoint as the interface sends it.
/// </summary>
public sealed record ConfigRequest(
    string? Name,
    string? Host,
    int ListenPort,
    string[]? Address,
    string[]? Dns,
    string[]? AllowedIps,
    int Mtu,
    int Keepalive,
    bool? IsEnabled,
    bool? Nat,
    bool? Opened,
    string[]? Blocked,
    string? PrivateKey,
    string? PresharedKey,
    ObfuscationBody? Obfuscation,
    int? OfflineAfter = null,
    string? Inbound = null,
    long? TemplateId = null);

/// <summary>
/// What a request to turn an endpoint on or off carries.
/// </summary>
/// <param name="On">Whether the endpoint runs.</param>
public sealed record ConfigSwitchRequest(bool? On);

/// <summary>
/// What putting an endpoint on the host produced, as the interface reads it.
/// </summary>
public sealed record ConfigSyncResponse(string Name, bool IsDone, string Message);

/// <summary>
/// The refusal of the host to raise an endpoint the panel kept and turned off.
/// </summary>
public sealed record ConfigRaiseFailure(string Error, string Message, long Id);

/// <summary>
/// The interface file an endpoint is read from.
/// </summary>
public sealed record ConfigImportRequest(string? Name, string? Text);

/// <summary>
/// A key pair as the interface reads it.
/// </summary>
public sealed record KeyPairResponse(string PrivateKey, string PublicKey);

/// <summary>
/// A single key as the interface reads it.
/// </summary>
public sealed record KeyResponse(string Key);

/// <summary>
/// Turns endpoints between the shape the panel holds and the shape the interface reads.
/// </summary>
public static class ConfigAnswers
{
    /// <summary>
    /// Describes an endpoint as the interface reads it, with the private key only for a caller that changes it.
    /// </summary>
    public static ConfigResponse Config(ServerConfig config, bool secrets) => new(
        config.Id,
        config.TemplateId,
        config.Name,
        config.Host,
        config.ListenPort,
        [.. config.Address],
        [.. config.Dns],
        [.. config.AllowedIps],
        config.Mtu,
        config.Keepalive,
        config.OfflineAfter,
        config.IsEnabled,
        config.Nat,
        config.Opened,
        InboundName.Of(config.Inbound),
        [.. config.Blocked],
        config.PublicKey,
        secrets ? config.PrivateKey : null,
        secrets ? config.PresharedKey : null,
        Obfuscation(config.Obfuscation),
        config.CreatedUtc,
        config.UpdatedUtc);

    /// <summary>
    /// Describes the obfuscation of an endpoint as the interface reads it.
    /// </summary>
    public static ObfuscationBody Obfuscation(ObfuscationSettings settings) => new(
        settings.Jc,
        settings.Jmin,
        settings.Jmax,
        settings.S1,
        settings.S2,
        settings.S3,
        settings.S4,
        settings.H1,
        settings.H2,
        settings.H3,
        settings.H4,
        settings.I1,
        settings.I2,
        settings.I3,
        settings.I4,
        settings.I5,
        settings.HeaderProtectionKey,
        settings.ContentPaddingAddition,
        settings.RekeyAfterTime,
        settings.RekeyTimeout,
        settings.RejectAfterTime,
        settings.KeepaliveTimeout,
        settings.MaxHandshakeAttempts,
        settings.RandomTrailers,
        settings.DisableCookies);

    /// <summary>
    /// Reads the endpoint an interface sends.
    /// </summary>
    public static ServerConfig Draft(ConfigRequest request) => new()
    {
        TemplateId = request.TemplateId,
        Name = (request.Name ?? string.Empty).Trim(),
        Host = (request.Host ?? string.Empty).Trim(),
        ListenPort = request.ListenPort,
        Address = request.Address ?? [],
        Dns = request.Dns ?? [],
        AllowedIps = request.AllowedIps ?? [],
        Mtu = request.Mtu,
        Keepalive = request.Keepalive,
        OfflineAfter = request.OfflineAfter ?? ConfigDefaults.OfflineAfter,
        IsEnabled = request.IsEnabled ?? true,
        Nat = request.Nat ?? true,
        Opened = request.Opened ?? false,
        Inbound = InboundName.Read(request.Inbound, ClientInbound.Off),
        Blocked = request.Blocked ?? [],
        PrivateKey = (request.PrivateKey ?? string.Empty).Trim(),
        PresharedKey = (request.PresharedKey ?? string.Empty).Trim(),
        Obfuscation = Settings(request.Obfuscation),
    };

    /// <summary>
    /// Reads the obfuscation an interface sends.
    /// </summary>
    public static ObfuscationSettings Settings(ObfuscationBody? body) => body is null
        ? new ObfuscationSettings()
        : new ObfuscationSettings
        {
            Jc = body.Jc,
            Jmin = body.Jmin,
            Jmax = body.Jmax,
            S1 = body.S1,
            S2 = body.S2,
            S3 = body.S3,
            S4 = body.S4,
            H1 = Text(body.H1),
            H2 = Text(body.H2),
            H3 = Text(body.H3),
            H4 = Text(body.H4),
            I1 = body.I1,
            I2 = body.I2,
            I3 = body.I3,
            I4 = body.I4,
            I5 = body.I5,
            HeaderProtectionKey = Text(body.HeaderProtectionKey),
            ContentPaddingAddition = Text(body.ContentPaddingAddition),
            RekeyAfterTime = Text(body.RekeyAfterTime),
            RekeyTimeout = Text(body.RekeyTimeout),
            RejectAfterTime = Text(body.RejectAfterTime),
            KeepaliveTimeout = Text(body.KeepaliveTimeout),
            MaxHandshakeAttempts = Text(body.MaxHandshakeAttempts),
            RandomTrailers = body.RandomTrailers,
            DisableCookies = body.DisableCookies,
        };

    private static string Text(string? value) => (value ?? string.Empty).Trim();
}
