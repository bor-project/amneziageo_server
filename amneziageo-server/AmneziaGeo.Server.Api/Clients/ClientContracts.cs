using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Traffic;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// What the host carries for one client, the addresses a second device of it is cut off at and the traffic it makes.
/// </summary>
public sealed record ClientStateBody(
    bool IsOnline,
    bool IsPresent,
    DateTimeOffset? LastHandshake,
    ulong RxBytes,
    ulong TxBytes,
    string Endpoint,
    IReadOnlyList<string> Cut,
    double RxRate,
    double TxRate,
    ulong TodayRx,
    ulong TodayTx,
    ulong Used,
    bool IsSpent);

/// <summary>
/// A signal to disconnect the application of a client did not take.
/// </summary>
/// <param name="At">When the signal was sent.</param>
/// <param name="Error">The code of the failure.</param>
/// <param name="Message">What went wrong, in words.</param>
public sealed record ClientSignalBody(DateTimeOffset At, string Error, string Message);

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
    long? TemplateId,
    string SubscriptionId,
    long DailyLimit,
    string Inbound,
    bool Disconnect,
    bool DisconnectRefused,
    ClientStateBody State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    ClientSignalBody? Signal = null,
    string DnsMode = DnsChoiceName.Auto,
    IReadOnlyList<string>? Dns = null);

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
    bool? IsEnabled,
    string? Note,
    long? TemplateId = null,
    string? SubscriptionId = null,
    long? DailyLimit = null,
    string? Inbound = null,
    bool? Disconnect = null,
    string? DnsMode = null,
    IReadOnlyList<string>? Dns = null,
    IReadOnlyList<long>? Rules = null);

/// <summary>
/// Whether a client is on.
/// </summary>
public sealed record ClientSwitchRequest(bool? On);

/// <summary>
/// The clients a command goes over, and whether they end up on.
/// </summary>
public sealed record ClientBatchRequest(IReadOnlyList<long>? Ids, bool? On = null);

/// <summary>
/// The clients a command over many of them went through with, the ones it refused and the endpoints that did not
/// take the change.
/// </summary>
public sealed record ClientBatchBody(IReadOnlyList<long> Done, IReadOnlyList<ClientBatchFault> Failed, IReadOnlyList<ClientSyncFault> Unsynced);

/// <summary>
/// An endpoint that did not take a change, and why.
/// </summary>
public sealed record ClientSyncFault(string Config, string Message);

/// <summary>
/// The text the clients of an endpoint are imported from.
/// </summary>
public sealed record ClientImportRequest(long ConfigId, string? Text, string? Prefix = null);

/// <summary>
/// The configuration a client connects with, the address of its subscription, why the address is empty, and what keeps
/// the certificate of the subscription and the one of the panel from being taken by every client.
/// </summary>
public sealed record ClientConfigResponse(
    string FileName,
    string Text,
    string Link,
    string Subscription,
    string SubscriptionMiss,
    string SubscriptionFault = "",
    string SubscriptionDetail = "",
    string PanelFault = "",
    string PanelDetail = "");

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
    /// Returns one client with what the host carries for it and the traffic it makes.
    /// </summary>
    public static ClientResponse Client(
        TunnelClient client,
        string config,
        ClientState state,
        bool secrets,
        IReadOnlyList<string> cut,
        ClientTraffic traffic)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cut);
        ArgumentNullException.ThrowIfNull(traffic);

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
            client.TemplateId,
            secrets ? client.SubscriptionId : string.Empty,
            client.DailyLimit,
            InboundName.Of(client.Inbound),
            client.Disconnect,
            client.DisconnectRefused,
            new ClientStateBody(
                state.IsOnline,
                state.IsPresent,
                state.LastHandshake,
                state.RxBytes,
                state.TxBytes,
                state.Endpoint,
                cut,
                Math.Round(traffic.Rate.Rx, 1),
                Math.Round(traffic.Rate.Tx, 1),
                traffic.Used.Rx,
                traffic.Used.Tx,
                traffic.Used.Total,
                traffic.IsSpent),
            client.CreatedUtc,
            client.UpdatedUtc,
            DnsMode: DnsChoiceName.Of(client.DnsMode),
            Dns: client.Dns);
    }

    /// <summary>
    /// Returns what a request asks a client to become, keeping what the client held where the request names nothing.
    /// </summary>
    public static TunnelClient Draft(ClientRequest request, TunnelClient? held)
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
            IsEnabled = request.IsEnabled ?? held?.IsEnabled ?? true,
            Note = (request.Note ?? string.Empty).Trim(),
            TemplateId = request.TemplateId,
            DnsMode = DnsChoiceName.Read(request.DnsMode, held?.DnsMode ?? DnsChoice.Auto),
            Dns = request.Dns is null ? held?.Dns ?? [] : Clean(request.Dns),
            SubscriptionId = request.SubscriptionId?.Trim() ?? held?.SubscriptionId ?? ClientDefaults.SubscriptionId(),
            DailyLimit = request.DailyLimit ?? held?.DailyLimit ?? 0,
            Inbound = InboundName.Read(request.Inbound, held?.Inbound ?? ClientInbound.Endpoint),
            Disconnect = request.Disconnect ?? held?.Disconnect ?? true,
        };
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
    [
        .. (values ?? [])
            .SelectMany(value => value.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    ];
}
