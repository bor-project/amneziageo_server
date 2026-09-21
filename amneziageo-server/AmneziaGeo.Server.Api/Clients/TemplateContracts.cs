using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// One client template as the panel reads it.
/// </summary>
public sealed record TemplateResponse(
    long Id,
    string Name,
    IReadOnlyList<string> Entries,
    IReadOnlyList<string> AllowedIps,
    IReadOnlyList<string> Missed,
    IReadOnlyList<string> Dns,
    int? Mtu,
    int? Keepalive,
    bool Routing,
    int Clients,
    DateTimeOffset? RefreshedUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// The settings a client template is added or changed with.
/// </summary>
public sealed record TemplateRequest(
    string? Name,
    IReadOnlyList<string>? Entries,
    IReadOnlyList<string>? Dns,
    int? Mtu,
    int? Keepalive,
    bool? Routing = null);

/// <summary>
/// The entries the panel asks what they come out as.
/// </summary>
public sealed record TemplatePreviewRequest(IReadOnlyList<string>? Entries);

/// <summary>
/// What one entry of a template gives.
/// </summary>
public sealed record TemplatePartResponse(string Entry, int Total, IReadOnlyList<string> AllowedIps);

/// <summary>
/// What the entries of a template give before it is kept.
/// </summary>
public sealed record TemplatePreviewResponse(
    int Total,
    IReadOnlyList<string> AllowedIps,
    IReadOnlyList<string> Missed,
    IReadOnlyList<TemplatePartResponse> Parts);

/// <summary>
/// The values a client file takes where its template names none.
/// </summary>
public sealed record TemplateDefaultsResponse(
    IReadOnlyList<string> AllowedIps,
    IReadOnlyList<string> Dns,
    int Mtu,
    int Keepalive,
    bool Routing);

/// <summary>
/// Turns client templates into what the panel reads and back.
/// </summary>
public static class TemplateAnswers
{
    /// <summary>
    /// The most ranges one preview carries.
    /// </summary>
    public const int MaxShown = 1000;

    /// <summary>
    /// The most ranges one entry of a preview carries.
    /// </summary>
    public const int MaxPart = 200;

    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Returns one template with the number of clients that take it.
    /// </summary>
    public static TemplateResponse Template(ClientTemplate template, int clients)
    {
        ArgumentNullException.ThrowIfNull(template);

        return new TemplateResponse(
            template.Id,
            template.Name,
            template.Entries,
            template.AllowedIps,
            template.Missed,
            template.Dns,
            template.Mtu,
            template.Keepalive,
            template.Routing,
            clients,
            template.RefreshedUtc,
            template.CreatedUtc,
            template.UpdatedUtc);
    }

    /// <summary>
    /// Returns the values a template gives where it names none.
    /// </summary>
    public static TemplateDefaultsResponse Defaults(IEnumerable<string> address) =>
        new(
            TemplateDefaults.AllowedIps(address),
            TemplateDefaults.Dns,
            TemplateDefaults.Mtu,
            TemplateDefaults.Keepalive,
            TemplateDefaults.Routing);

    /// <summary>
    /// Returns what a request asks a template to become.
    /// </summary>
    public static ClientTemplate Draft(TemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ClientTemplate
        {
            Name = (request.Name ?? string.Empty).Trim(),
            Entries = Entries(request.Entries),
            Dns = Clean(request.Dns),
            Mtu = request.Mtu,
            Keepalive = request.Keepalive,
            Routing = request.Routing ?? TemplateDefaults.Routing,
        };
    }

    /// <summary>
    /// Returns the entries written the way a template keeps them.
    /// </summary>
    public static IReadOnlyList<string> Entries(IReadOnlyList<string>? values) =>
        [.. Clean(values).Select(one => TemplateList.Entry(one) ?? one).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Returns what the entries gave, the long lists cut to what one answer carries.
    /// </summary>
    public static TemplatePreviewResponse Preview(TemplateResolution found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return new TemplatePreviewResponse(
            found.AllowedIps.Count,
            [.. found.AllowedIps.Take(MaxShown)],
            found.Missed,
            [.. found.Parts.Select(Part)]);
    }

    private static TemplatePartResponse Part(TemplatePart part) =>
        new(part.Entry, part.AllowedIps.Count, [.. part.AllowedIps.Take(MaxPart)]);

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
    [
        .. (values ?? [])
            .SelectMany(value => value.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    ];
}
