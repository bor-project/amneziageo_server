using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Api.Proxy;

/// <summary>
/// A proxy template as the interface reads it.
/// </summary>
public sealed record ProxyTemplateResponse(
    long Id,
    string Name,
    string Kind,
    int Port,
    bool Opened,
    bool MakePath,
    string Target,
    string[] Sources,
    int Proxies,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// A proxy template as the interface sends it.
/// </summary>
public sealed record ProxyTemplateRequest(
    string? Name,
    string? Kind,
    int Port,
    bool? Opened,
    bool? MakePath,
    string? Target,
    string[]? Sources);

/// <summary>
/// A kept proxy template with what starting its proxies produced.
/// </summary>
public sealed record ProxyTemplateSaveResponse(ProxyTemplateResponse Template, ConfigSyncResponse[] Proxies);

/// <summary>
/// Turns proxy templates between the shape the panel holds and the shape the interface reads.
/// </summary>
public static class ProxyTemplateAnswers
{
    /// <summary>
    /// Describes one template with the number of proxies that take it.
    /// </summary>
    public static ProxyTemplateResponse Template(ProxyTemplate template, int proxies)
    {
        ArgumentNullException.ThrowIfNull(template);

        return new ProxyTemplateResponse(
            template.Id,
            template.Name,
            template.Kind,
            template.Port,
            template.Opened,
            template.MakePath,
            template.Target,
            [.. template.Sources],
            proxies,
            template.CreatedUtc,
            template.UpdatedUtc);
    }

    /// <summary>
    /// Reads the template an interface sends.
    /// </summary>
    public static ProxyTemplate Draft(ProxyTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ProxyTemplate
        {
            Name = (request.Name ?? string.Empty).Trim(),
            Kind = (request.Kind ?? ProxyKind.Ws).Trim(),
            Port = request.Port,
            Opened = request.Opened ?? false,
            MakePath = request.MakePath ?? true,
            Target = (request.Target ?? string.Empty).Trim(),
            Sources = PanelList.Of(request.Sources ?? []),
        };
    }
}
