using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// What the host carries for one client.
/// </summary>
public sealed record ClientStateBody(
    bool IsOnline,
    bool IsPresent,
    DateTimeOffset? LastHandshake,
    ulong RxBytes,
    ulong TxBytes,
    string Endpoint);

/// <summary>
/// One client as the panel reads it.
/// </summary>
public sealed record ClientResponse(
    long Id,
    long ConfigId,
    string Config,
    string Name,
    string PublicKey,
    string PrivateKey,
    string PresharedKey,
    IReadOnlyList<string> Address,
    bool IsEnabled,
    string Note,
    ClientStateBody State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// The settings a client is added or changed with.
/// </summary>
public sealed record ClientRequest(
    long ConfigId,
    string? Name,
    string? PrivateKey,
    string? PublicKey,
    string? PresharedKey,
    IReadOnlyList<string>? Address,
    bool IsEnabled,
    string? Note);

/// <summary>
/// Whether a client is on.
/// </summary>
public sealed record ClientSwitchRequest(bool On);

/// <summary>
/// The configuration a client connects with.
/// </summary>
public sealed record ClientConfigResponse(string FileName, string Text);

/// <summary>
/// What putting the clients of one endpoint on the host produced.
/// </summary>
public sealed record ClientApplyBody(string Config, bool IsDone, int Clients, IReadOnlyList<string> Stale, string Message);

/// <summary>
/// Turns clients into what the panel reads and back.
/// </summary>
public static class ClientAnswers
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Returns one client with what the host carries for it.
    /// </summary>
    public static ClientResponse Client(TunnelClient client, string config, ClientState state, bool secrets)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(state);

        return new ClientResponse(
            client.Id,
            client.ConfigId,
            config,
            client.Name,
            client.PublicKey,
            secrets ? client.PrivateKey : string.Empty,
            secrets ? client.PresharedKey : string.Empty,
            client.Address,
            client.IsEnabled,
            client.Note,
            new ClientStateBody(
                state.IsOnline,
                state.IsPresent,
                state.LastHandshake,
                state.RxBytes,
                state.TxBytes,
                state.Endpoint),
            client.CreatedUtc,
            client.UpdatedUtc);
    }

    /// <summary>
    /// Returns what a request asks a client to become.
    /// </summary>
    public static TunnelClient Draft(ClientRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new TunnelClient
        {
            ConfigId = request.ConfigId,
            Name = (request.Name ?? string.Empty).Trim(),
            PrivateKey = (request.PrivateKey ?? string.Empty).Trim(),
            PublicKey = (request.PublicKey ?? string.Empty).Trim(),
            PresharedKey = (request.PresharedKey ?? string.Empty).Trim(),
            Address = Clean(request.Address),
            IsEnabled = request.IsEnabled,
            Note = (request.Note ?? string.Empty).Trim(),
        };
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
    [
        .. (values ?? [])
            .SelectMany(value => value.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    ];
}
