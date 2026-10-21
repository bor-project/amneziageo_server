using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// One routing preset as the panel reads it.
/// </summary>
public sealed record PresetResponse(
    long Id,
    string Uid,
    string Name,
    IReadOnlyList<string> Proxy,
    IReadOnlyList<string> Direct,
    IReadOnlyList<string> Block,
    bool AllUdp,
    bool Full,
    bool IsDefault,
    int Templates,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<long>? TemplateIds = null);

/// <summary>
/// The lists a routing preset is added or changed with.
/// </summary>
public sealed record PresetRequest(
    string? Name,
    IReadOnlyList<string>? Proxy,
    IReadOnlyList<string>? Direct,
    IReadOnlyList<string>? Block,
    bool? AllUdp = null,
    bool? Full = null,
    bool? IsDefault = null,
    IReadOnlyList<long>? Templates = null);

/// <summary>
/// Turns routing presets into what the panel reads and back.
/// </summary>
public static class PresetAnswers
{
    /// <summary>
    /// Returns one preset with the templates that name it.
    /// </summary>
    public static PresetResponse Preset(RoutingPreset preset, IReadOnlyList<long> templates)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(templates);

        return new PresetResponse(
            preset.Id,
            preset.Uid,
            preset.Name,
            preset.Proxy,
            preset.Direct,
            preset.Block,
            preset.AllUdp,
            preset.Full,
            preset.IsDefault,
            templates.Count,
            preset.CreatedUtc,
            preset.UpdatedUtc,
            templates);
    }

    /// <summary>
    /// Returns what a request asks a preset to become; a request that leaves the default mark out keeps the one held.
    /// </summary>
    public static RoutingPreset Draft(PresetRequest request, RoutingPreset? held = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RoutingPreset
        {
            Name = (request.Name ?? string.Empty).Trim(),
            Proxy = TemplateAnswers.Entries(request.Proxy),
            Direct = TemplateAnswers.Entries(request.Direct),
            Block = TemplateAnswers.Entries(request.Block),
            AllUdp = request.AllUdp ?? false,
            Full = request.Full ?? false,
            IsDefault = request.IsDefault ?? held?.IsDefault ?? false,
        };
    }

    /// <summary>
    /// Returns the templates a request hands the preset to, null when it leaves them as they are.
    /// </summary>
    public static IReadOnlyList<long>? Holders(PresetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Templates is null ? null : [.. request.Templates.Distinct()];
    }
}
