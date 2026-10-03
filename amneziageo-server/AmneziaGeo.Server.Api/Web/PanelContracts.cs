using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Where the panel answers: the port, the path and whether it speaks TLS.
/// </summary>
public sealed record PanelPlace(int Port, string Path, bool Secure);

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
    bool Pending,
    bool Secure,
    PanelPlace Running);

/// <summary>
/// The settings the panel is changed with; keep leaves the port the panel held open before it moved open in the
/// firewall of the host, as a rule of the host, instead of closing it.
/// </summary>
public sealed record PanelRequest(
    IReadOnlyList<string>? Listen,
    IReadOnlyList<string>? Domains,
    int Port,
    bool? Opened,
    string? Path,
    string? Certificate,
    string? CertificateKey,
    string? Language,
    bool Prereleases,
    string? NameTemplate = null,
    bool? Keep = null);

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
    /// Returns the settings with what the host offers to pick from, whether they wait for a restart, whether the panel
    /// speaks TLS under them and where it answers now.
    /// </summary>
    public static PanelResponse Panel(PanelSettings settings, PanelSettings running, WebOptions options, bool secure, PanelPlace answering)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(answering);

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
            settings.Differs(running),
            secure,
            answering);
    }

    /// <summary>
    /// Returns what a request asks the panel to become; a request without opened keeps the one held.
    /// </summary>
    public static PanelSettings Draft(PanelRequest request, PanelSettings? held = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PanelSettings
        {
            Listen = PanelList.Of(request.Listen),
            Domains = PanelList.Of(request.Domains),
            Port = request.Port,
            Opened = request.Opened ?? held?.Opened ?? false,
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
