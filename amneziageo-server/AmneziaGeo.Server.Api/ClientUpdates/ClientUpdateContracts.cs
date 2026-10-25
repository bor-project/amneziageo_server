using System.Globalization;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Updates;

namespace AmneziaGeo.Server.Api.ClientUpdates;

/// <summary>
/// The settings of the releases of the client as the panel reads them, with what the host and the service apart say.
/// </summary>
/// <param name="IsEnabled">Whether the panel offers the releases.</param>
/// <param name="Source">Where the releases come from: own or address.</param>
/// <param name="Address">The address of the service apart.</param>
/// <param name="Token">The token of the store of the panel, empty for a service apart.</param>
/// <param name="Directory">The directory of the store of the panel, empty for the one beside the database.</param>
/// <param name="Port">The port of the store of the panel, 0 for the port of the panel.</param>
/// <param name="Path">The path of the store of the panel.</param>
/// <param name="Default">Whether a client takes this server for its updates before it picks one itself.</param>
/// <param name="Fault">Why the host refused to serve the releases, empty when it did not.</param>
/// <param name="Detail">What the host said about the refusal.</param>
/// <param name="AddressFault">Why the service apart did not answer, empty when it did.</param>
/// <param name="Pin">The SHA-256 of the certificate the service apart answered under.</param>
/// <param name="Checked">When the service apart was last asked.</param>
/// <param name="Status">What the store says about itself, null while it is not reached.</param>
/// <param name="PanelPort">The port of the panel.</param>
/// <param name="DefaultDirectory">The directory the store takes when the settings name none.</param>
/// <param name="Url">The address the clients read the releases at.</param>
public sealed record ClientUpdateResponse(
    bool IsEnabled,
    string Source,
    string Address,
    string Token,
    string Directory,
    int Port,
    string Path,
    bool Default,
    string Fault,
    string Detail,
    string AddressFault,
    string Pin,
    DateTimeOffset? Checked,
    StoreStatus? Status,
    int PanelPort,
    string DefaultDirectory,
    string Url);

/// <summary>
/// The settings the releases of the client are changed with; a token left out keeps the one held.
/// </summary>
public sealed record ClientUpdateRequest(
    bool IsEnabled,
    string? Source,
    string? Address,
    string? Token,
    string? Directory,
    int? Port,
    string? Path,
    bool? Default);

/// <summary>
/// Turns the settings of the releases into what the panel reads and back.
/// </summary>
public static class ClientUpdateAnswers
{
    /// <summary>
    /// Returns the settings with what the host and the service apart say about them.
    /// </summary>
    public static ClientUpdateResponse Settings(ClientUpdateSettings settings, ClientUpdateState state, StoreStatus? status, PanelSettings panel, string host)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(panel);

        return new ClientUpdateResponse(
            settings.IsEnabled,
            settings.Source,
            settings.Address,
            settings.Own ? settings.Token : string.Empty,
            settings.Directory,
            settings.Port,
            settings.Path,
            settings.Default,
            state.Fault,
            state.Detail,
            state.AddressFault,
            state.Pin,
            state.Checked,
            status,
            panel.Port,
            ClientUpdateServer.DefaultDirectory,
            Url(settings, panel, host));
    }

    /// <summary>
    /// Returns what a request asks the releases to become; a request without a token keeps the one held.
    /// </summary>
    public static ClientUpdateSettings Draft(ClientUpdateRequest request, ClientUpdateSettings held)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(held);

        return new ClientUpdateSettings
        {
            IsEnabled = request.IsEnabled,
            Source = Trim(request.Source) is { Length: > 0 } source ? source : ClientUpdateDefaults.Own,
            Address = Trim(request.Address),
            Token = request.Token is null ? held.Token : request.Token.Trim(),
            Directory = Trim(request.Directory),
            Port = request.Port ?? 0,
            Path = Trim(request.Path).Trim('/'),
            Default = request.Default ?? true,
        };
    }

    /// <summary>
    /// Returns the address the clients read the releases at, for the host the panel was asked through.
    /// </summary>
    public static string Url(ClientUpdateSettings settings, PanelSettings panel, string host)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(host);

        if (!settings.IsEnabled)
        {
            return string.Empty;
        }

        if (!settings.Own)
        {
            return settings.Address.Length > 0 ? settings.Address.TrimEnd('/') + "/" : string.Empty;
        }

        var named = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;

        return string.Create(CultureInfo.InvariantCulture, $"https://{named}:{settings.PortOr(panel.Port)}{settings.Prefix}/");
    }

    private static string Trim(string? value) => value is null ? string.Empty : value.Trim();
}
