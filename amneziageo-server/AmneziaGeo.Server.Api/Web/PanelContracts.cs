using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// The settings of the panel as it reads them.
/// </summary>
public sealed record PanelResponse(
    IReadOnlyList<string> Listen,
    IReadOnlyList<string> Domains,
    int Port,
    string Path,
    string Certificate,
    string CertificateKey,
    string Language,
    IReadOnlyList<string> Certificates,
    IReadOnlyList<string> Addresses,
    string CertificateRoot);

/// <summary>
/// The settings the panel is changed with.
/// </summary>
public sealed record PanelRequest(
    IReadOnlyList<string>? Listen,
    IReadOnlyList<string>? Domains,
    int Port,
    string? Path,
    string? Certificate,
    string? CertificateKey,
    string? Language);

/// <summary>
/// Turns the settings of the panel into what it reads and back.
/// </summary>
public static class PanelAnswers
{
    /// <summary>
    /// Returns the settings with what the host offers to pick from.
    /// </summary>
    public static PanelResponse Panel(PanelSettings settings, WebOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        return new PanelResponse(
            settings.Listen,
            settings.Domains,
            settings.Port,
            settings.Prefix,
            settings.Certificate,
            settings.CertificateKey,
            settings.Language,
            PanelChoices.Domains(options.CertificateRoot),
            PanelChoices.Addresses(),
            options.CertificateRoot);
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
            Path = Trim(request.Path).Trim('/'),
            Certificate = Trim(request.Certificate),
            CertificateKey = Trim(request.CertificateKey),
            Language = request.Language is { Length: > 0 } language ? language : PanelDefaults.Language,
        };
    }

    private static string Trim(string? value) => value is null ? string.Empty : value.Trim();
}
