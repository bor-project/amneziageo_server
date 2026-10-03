using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// One routing preset as the panel reads it.
/// </summary>
public sealed record PresetResponse(
    long Id,
    string Name,
    IReadOnlyList<string> Proxy,
    IReadOnlyList<string> Direct,
    IReadOnlyList<string> Block,
    bool AllUdp,
    bool Full,
    int Templates,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// The lists a routing preset is added or changed with.
/// </summary>
public sealed record PresetRequest(
    string? Name,
    IReadOnlyList<string>? Proxy,
    IReadOnlyList<string>? Direct,
    IReadOnlyList<string>? Block,
    bool? AllUdp = null,
    bool? Full = null);

/// <summary>
/// Turns routing presets into what the panel reads and back.
/// </summary>
public static class PresetAnswers
{
    /// <summary>
    /// Returns one preset with the number of templates that name it.
    /// </summary>
    public static PresetResponse Preset(RoutingPreset preset, int templates)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return new PresetResponse(
            preset.Id,
            preset.Name,
            preset.Proxy,
            preset.Direct,
            preset.Block,
            preset.AllUdp,
            preset.Full,
            templates,
            preset.CreatedUtc,
            preset.UpdatedUtc);
    }

    /// <summary>
    /// Returns what a request asks a preset to become.
    /// </summary>
    public static RoutingPreset Draft(PresetRequest request)
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
        };
    }
}
