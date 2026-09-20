using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// An endpoint template as the interface reads it.
/// </summary>
public sealed record InterfaceTemplateResponse(
    long Id,
    string Name,
    int ListenPort,
    string Subnet,
    string[] Dns,
    string[] AllowedIps,
    int Mtu,
    int Keepalive,
    int OfflineAfter,
    string[] Blocked,
    long? ClientTemplateId,
    ObfuscationBody Obfuscation,
    int Configs,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// An endpoint template as the interface sends it.
/// </summary>
public sealed record InterfaceTemplateRequest(
    string? Name,
    int ListenPort,
    string? Subnet,
    string[]? Dns,
    string[]? AllowedIps,
    int Mtu,
    int Keepalive,
    string[]? Blocked,
    long? ClientTemplateId,
    ObfuscationBody? Obfuscation,
    int? OfflineAfter = null);

/// <summary>
/// A kept endpoint template with what putting its endpoints on the host produced.
/// </summary>
public sealed record InterfaceTemplateSaveResponse(
    InterfaceTemplateResponse Template,
    ConfigSyncResponse[] Configs);

/// <summary>
/// Turns endpoint templates between the shape the panel holds and the shape the interface reads.
/// </summary>
public static class InterfaceTemplateAnswers
{
    /// <summary>
    /// Describes one template with the number of endpoints that take it.
    /// </summary>
    public static InterfaceTemplateResponse Template(InterfaceTemplate template, int configs)
    {
        ArgumentNullException.ThrowIfNull(template);

        return new InterfaceTemplateResponse(
            template.Id,
            template.Name,
            template.ListenPort,
            template.Subnet,
            [.. template.Dns],
            [.. template.AllowedIps],
            template.Mtu,
            template.Keepalive,
            template.OfflineAfter,
            [.. template.Blocked],
            template.ClientTemplateId,
            ConfigAnswers.Obfuscation(template.Obfuscation),
            configs,
            template.CreatedUtc,
            template.UpdatedUtc);
    }

    /// <summary>
    /// Reads the template an interface sends.
    /// </summary>
    public static InterfaceTemplate Draft(InterfaceTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new InterfaceTemplate
        {
            Name = (request.Name ?? string.Empty).Trim(),
            ListenPort = request.ListenPort,
            Subnet = (request.Subnet ?? ConfigDefaults.Subnet).Trim(),
            Dns = request.Dns ?? [],
            AllowedIps = request.AllowedIps ?? [],
            Mtu = request.Mtu,
            Keepalive = request.Keepalive,
            OfflineAfter = request.OfflineAfter ?? ConfigDefaults.OfflineAfter,
            Blocked = request.Blocked ?? [],
            ClientTemplateId = request.ClientTemplateId,
            Obfuscation = ConfigAnswers.Settings(request.Obfuscation),
        };
    }
}
