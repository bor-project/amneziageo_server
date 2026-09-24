using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// The settings of the panel as it reads them.
/// </summary>
public sealed record PanelResponse(
    IReadOnlyList<string> Listen,
    IReadOnlyList<string> Domains,
    int Port,
    bool Opened,
    string Path,
    string Certificate,
    string CertificateKey,
    string Language,
    bool Prereleases,
    string NameTemplate,
    IReadOnlyList<string> Certificates,
    IReadOnlyList<string> Addresses,
    string CertificateRoot,
    bool Pending);

/// <summary>
/// The settings the panel is changed with.
/// </summary>
public sealed record PanelRequest(
    IReadOnlyList<string>? Listen,
    IReadOnlyList<string>? Domains,
    int Port,
    bool Opened,
    string? Path,
    string? Certificate,
    string? CertificateKey,
    string? Language,
    bool Prereleases,
    string? NameTemplate = null);

/// <summary>
/// What the substitutions of the name template stand for with the first client, and the name it takes when nothing is
/// left of the template.
/// </summary>
public sealed record NameSample(IReadOnlyDictionary<string, string> Values, string Stamp);

/// <summary>
/// Turns the settings of the panel into what it reads and back.
/// </summary>
public static class PanelAnswers
{
    /// <summary>
    /// Returns the settings with what the host offers to pick from and whether they wait for a restart.
    /// </summary>
    public static PanelResponse Panel(PanelSettings settings, PanelSettings running, WebOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(options);

        return new PanelResponse(
            settings.Listen,
            settings.Domains,
            settings.Port,
            settings.Opened,
            settings.Prefix,
            settings.Certificate,
            settings.CertificateKey,
            settings.Language,
            settings.Prereleases,
            settings.NameTemplate,
            PanelChoices.Domains(options.CertificateRoot),
            PanelChoices.Addresses(),
            options.CertificateRoot,
            settings.Differs(running));
    }

    /// <summary>
    /// Returns what a request asks the panel to become.
    /// </summary>
    public static PanelSettings Draft(PanelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PanelSettings
        {
            Listen = PanelList.Of(request.Listen),
            Domains = PanelList.Of(request.Domains),
            Port = request.Port,
            Opened = request.Opened,
            Path = Trim(request.Path).Trim('/'),
            Certificate = Trim(request.Certificate),
            CertificateKey = Trim(request.CertificateKey),
            Language = request.Language is { Length: > 0 } language ? language : PanelDefaults.Language,
            Prereleases = request.Prereleases,
            NameTemplate = Trim(request.NameTemplate) is { Length: > 0 } named ? named : ConfigName.Default,
        };
    }

    private static string Trim(string? value) => value is null ? string.Empty : value.Trim();
}
