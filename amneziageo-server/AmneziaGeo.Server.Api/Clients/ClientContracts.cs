using AmneziaGeo.Server.Awg.Client;
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
/// One port of the host carried to a client.
/// </summary>
public sealed record ForwardBody(string Protocol, int From, int To);

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
    long? ParentId,
    bool MultiDevice,
    long DailyLimit,
    string Inbound,
    IReadOnlyList<string> Routes,
    IReadOnlyList<ForwardBody> Forwards,
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
    string? Note,
    long? TemplateId = null,
    string? SubscriptionId = null,
    bool? MultiDevice = null,
    long? DailyLimit = null,
    string? Inbound = null,
    IReadOnlyList<string>? Routes = null,
    IReadOnlyList<ForwardBody>? Forwards = null);

/// <summary>
/// Whether a client is on.
/// </summary>
public sealed record ClientSwitchRequest(bool? On);

/// <summary>
/// The text the clients of an endpoint are imported from.
/// </summary>
public sealed record ClientImportRequest(long ConfigId, string? Text, string? Prefix = null);

/// <summary>
/// The configuration a client connects with and the address of its subscription.
/// </summary>
public sealed record ClientConfigResponse(string FileName, string Text, string Link, string Subscription);

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
            client.ParentId,
            client.MultiDevice,
            client.DailyLimit,
            InboundName.Of(client.Inbound),
            client.Routes,
            [.. client.Forwards.Select(forward => new ForwardBody(forward.Protocol, forward.From, forward.To))],
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
                traffic.Group.Total,
                traffic.IsSpent),
            client.CreatedUtc,
            client.UpdatedUtc);
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
            IsEnabled = request.IsEnabled,
            Note = (request.Note ?? string.Empty).Trim(),
            TemplateId = request.TemplateId,
            SubscriptionId = request.SubscriptionId?.Trim() ?? held?.SubscriptionId ?? ClientDefaults.SubscriptionId(),
            MultiDevice = request.MultiDevice ?? held?.MultiDevice ?? false,
            DailyLimit = request.DailyLimit ?? held?.DailyLimit ?? 0,
            Inbound = InboundName.Read(request.Inbound, held?.Inbound ?? ClientInbound.Endpoint),
            Routes = request.Routes is null ? held?.Routes ?? [] : Clean(request.Routes),
            Forwards = request.Forwards is null ? held?.Forwards ?? [] : Carried(request.Forwards),
        };
    }

    private static IReadOnlyList<PortForward> Carried(IReadOnlyList<ForwardBody> bodies) =>
    [
        .. bodies.Select(body => new PortForward((body.Protocol ?? string.Empty).Trim().ToLowerInvariant(), body.From, body.To))
    ];

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
    [
        .. (values ?? [])
            .SelectMany(value => value.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    ];
}
