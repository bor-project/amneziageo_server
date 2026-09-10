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
    int? Keepalive);

/// <summary>
/// The values a client file takes where its template names none.
/// </summary>
public sealed record TemplateDefaultsResponse(
    IReadOnlyList<string> AllowedIps,
    IReadOnlyList<string> Dns,
    int Mtu,
    int Keepalive);

/// <summary>
/// Turns client templates into what the panel reads and back.
/// </summary>
public static class TemplateAnswers
{
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
            clients,
            template.RefreshedUtc,
            template.CreatedUtc,
            template.UpdatedUtc);
    }

    /// <summary>
    /// Returns the values a template gives where it names none.
    /// </summary>
    public static TemplateDefaultsResponse Defaults(IEnumerable<string> address) =>
        new(TemplateDefaults.AllowedIps(address), TemplateDefaults.Dns, TemplateDefaults.Mtu, TemplateDefaults.Keepalive);

    /// <summary>
    /// Returns what a request asks a template to become.
    /// </summary>
    public static ClientTemplate Draft(TemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ClientTemplate
        {
            Name = (request.Name ?? string.Empty).Trim(),
            Entries = [.. Clean(request.Entries).Select(one => TemplateList.Entry(one) ?? one).Distinct(StringComparer.Ordinal)],
            Dns = Clean(request.Dns),
            Mtu = request.Mtu,
            Keepalive = request.Keepalive,
        };
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
    [
        .. (values ?? [])
            .SelectMany(value => value.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    ];
}
