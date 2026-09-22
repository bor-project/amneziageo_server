using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// The settings of the subscriptions as the panel reads them.
/// </summary>
public sealed record SubscriptionResponse(
    bool IsEnabled,
    bool Separate,
    IReadOnlyList<string> Listen,
    IReadOnlyList<string> Domains,
    int Port,
    bool Opened,
    string Path,
    string Certificate,
    string CertificateKey,
    int UpdateHours,
    string Title,
    IReadOnlyList<string> Certificates,
    IReadOnlyList<string> Addresses,
    string CertificateRoot,
    string Fault);

/// <summary>
/// The settings the subscriptions are changed with.
/// </summary>
public sealed record SubscriptionRequest(
    bool IsEnabled,
    bool Separate,
    IReadOnlyList<string>? Listen,
    IReadOnlyList<string>? Domains,
    int Port,
    bool Opened,
    string? Path,
    string? Certificate,
    string? CertificateKey,
    int UpdateHours,
    string? Title);

/// <summary>
/// Turns the settings of the subscriptions into what the panel reads and back.
/// </summary>
public static class SubscriptionAnswers
{
    /// <summary>
    /// Returns the settings with what the host offers to pick from and why the host refused them.
    /// </summary>
    public static SubscriptionResponse Settings(SubscriptionSettings settings, string fault, WebOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(fault);
        ArgumentNullException.ThrowIfNull(options);

        return new SubscriptionResponse(
            settings.IsEnabled,
            settings.Separate,
            settings.Listen,
            settings.Domains,
            settings.Port,
            settings.Opened,
            settings.Prefix,
            settings.Certificate,
            settings.CertificateKey,
            settings.UpdateHours,
            settings.Title,
            PanelChoices.Domains(options.CertificateRoot),
            PanelChoices.Addresses(),
            options.CertificateRoot,
            fault);
    }

    /// <summary>
    /// Returns what a request asks the subscriptions to become.
    /// </summary>
    public static SubscriptionSettings Draft(SubscriptionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new SubscriptionSettings
        {
            IsEnabled = request.IsEnabled,
            Separate = request.Separate,
            Listen = PanelList.Of(request.Listen),
            Domains = PanelList.Of(request.Domains),
            Port = request.Port,
            Opened = request.Opened,
            Path = Trim(request.Path).Trim('/'),
            Certificate = Trim(request.Certificate),
            CertificateKey = Trim(request.CertificateKey),
            UpdateHours = request.UpdateHours,
            Title = Trim(request.Title),
        };
    }

    private static string Trim(string? value) => value is null ? string.Empty : value.Trim();
}
