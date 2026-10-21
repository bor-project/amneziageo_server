using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
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
    IReadOnlyList<long> Presets,
    bool StandIns,
    int Clients,
    DateTimeOffset? RefreshedUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string DnsMode = DnsChoiceName.Auto,
    IReadOnlyList<long>? ClientIds = null);

/// <summary>
/// The settings a client template is added or changed with.
/// </summary>
public sealed record TemplateRequest(
    string? Name,
    IReadOnlyList<string>? Entries,
    IReadOnlyList<string>? Dns,
    int? Mtu,
    int? Keepalive,
    IReadOnlyList<long>? Presets = null,
    bool? StandIns = null,
    string? DnsMode = null,
    IReadOnlyList<long>? Clients = null);

/// <summary>
/// The entries the panel asks what they come out as.
/// </summary>
public sealed record TemplatePreviewRequest(IReadOnlyList<string>? Entries, bool? StandIns = null);

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
    IReadOnlyList<long>? ClientIds = null);

/// <summary>
/// The clients that take no template.
/// </summary>
public sealed record TemplateClientsRequest(IReadOnlyList<long>? Clients);

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
    /// Returns one template with the clients that take it.
    /// </summary>
    public static TemplateResponse Template(ClientTemplate template, IReadOnlyList<long> clients)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(clients);

        return new TemplateResponse(
            template.Id,
            template.Name,
            template.Entries,
            template.AllowedIps,
            template.Missed,
            template.Dns,
            template.Mtu,
            template.Keepalive,
            template.Presets,
            template.StandIns,
            clients.Count,
            template.RefreshedUtc,
            template.CreatedUtc,
            template.UpdatedUtc,
            DnsChoiceName.Of(template.DnsMode),
            clients);
    }

    /// <summary>
    /// Returns the values a template gives where it names none, with the clients that take no template.
    /// </summary>
    public static TemplateDefaultsResponse Defaults(IEnumerable<string> address, IReadOnlyList<long> plain) =>
        new(
            TemplateDefaults.AllowedIps(address),
            TemplateDefaults.Dns,
            TemplateDefaults.Mtu,
            TemplateDefaults.Keepalive,
            plain);

    /// <summary>
    /// Returns the clients a request hands the template to, null when it leaves them as they are.
    /// </summary>
    public static IReadOnlyList<long>? Members(TemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Clients is null ? null : [.. request.Clients.Distinct()];
    }

    /// <summary>
    /// Returns what a request asks a template to become.
    /// </summary>
    public static ClientTemplate Draft(TemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dns = Clean(request.Dns);

        return new ClientTemplate
        {
            Name = (request.Name ?? string.Empty).Trim(),
            Entries = Entries(request.Entries),
            Dns = dns,
            DnsMode = DnsChoiceName.Read(request.DnsMode, dns.Count > 0 ? DnsChoice.Own : DnsChoice.Auto),
            Mtu = request.Mtu,
            Keepalive = request.Keepalive,
            Presets = [.. (request.Presets ?? []).Distinct()],
            StandIns = request.StandIns ?? false,
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
