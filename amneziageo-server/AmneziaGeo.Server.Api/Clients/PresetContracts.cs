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
    IReadOnlyList<long>? TemplateIds = null,
    bool ReadOnly = false);

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
    IReadOnlyList<long>? Templates = null,
    bool? ReadOnly = null);

/// <summary>
/// Turns routing presets into what the panel reads and back.
/// </summary>
public static class PresetAnswers
{
    private static readonly char[] Breaks = [',', ';', ' ', '\t', '\r'];

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
            templates,
            preset.ReadOnly);
    }

    /// <summary>
    /// Returns what a request asks a preset to become; a request that leaves the default or the read-only mark out
    /// keeps the one held.
    /// </summary>
    public static RoutingPreset Draft(PresetRequest request, RoutingPreset? held = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RoutingPreset
        {
            Name = (request.Name ?? string.Empty).Trim(),
            Proxy = Entries(request.Proxy),
            Direct = Entries(request.Direct),
            Block = Entries(request.Block),
            AllUdp = request.AllUdp ?? false,
            Full = request.Full ?? false,
            IsDefault = request.IsDefault ?? held?.IsDefault ?? false,
            ReadOnly = request.ReadOnly ?? held?.ReadOnly ?? false,
        };
    }

    /// <summary>
    /// Returns the entries written the way a preset keeps them: an application in a line ends only where another
    /// entry follows a comma or a semicolon, the rest of the line is split at commas, semicolons and spaces.
    /// </summary>
    public static IReadOnlyList<string> Entries(IReadOnlyList<string>? values) =>
    [
        .. (values ?? [])
            .SelectMany(value => value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .SelectMany(PresetList.Pieces)
            .SelectMany(piece => PresetList.IsApp(piece) ? [piece] : piece.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(one => PresetList.Entry(one) ?? one)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>
    /// Returns the templates a request hands the preset to, null when it leaves them as they are.
    /// </summary>
    public static IReadOnlyList<long>? Holders(PresetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Templates is null ? null : [.. request.Templates.Distinct()];
    }
}
