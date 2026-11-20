using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Traffic;

namespace AmneziaGeo.Server.Api.Clients;

public sealed record ClientInfoResponse(
    long Id,
    string Name,
    bool IsEnabled,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    ClientConnectionInfo Connection,
    ClientTrafficInfo Traffic,
    ClientLimitsInfo Limits,
    ClientConfigurationInfo? Configuration,
    ClientStatisticsInfo Statistics);

public sealed record ClientConnectionInfo(
    bool IsOnline,
    bool IsPresent,
    DateTimeOffset? LastHandshake,
    DateTimeOffset? LastActivity,
    DateTimeOffset? SessionStartedUtc,
    string? Endpoint,
    IReadOnlyList<string> Cut,
    string? Reason);

public sealed record ClientTrafficInfo(
    ulong? PeerRxBytes,
    ulong? PeerTxBytes,
    double? RxRate,
    double? TxRate,
    ulong? TodayRx,
    ulong? TodayTx,
    DateOnly Day,
    string TimeZone,
    IReadOnlyList<ClientTrafficDayInfo> History);

public sealed record ClientTrafficDayInfo(DateOnly Day, ulong RxBytes, ulong TxBytes);

public sealed record ClientLimitsInfo(
    ClientLimitStatus? Status,
    TrafficLimits? Policy,
    string Source,
    long? TemplateId,
    long LegacyDailyLimit);

public sealed record ClientConfigurationInfo(
    long Id,
    string Name,
    bool IsEnabled,
    IReadOnlyList<string> Address,
    long? TemplateId,
    string? TemplateName,
    string Endpoint,
    IReadOnlyList<string> Dns,
    string DnsSource,
    string DnsMode,
    IReadOnlyList<string> AllowedIps,
    string AllowedIpsSource,
    int AllowedIpsTotal,
    bool AllowedIpsTruncated,
    int Mtu,
    string MtuSource,
    int Keepalive,
    string KeepaliveSource,
    string Inbound,
    IReadOnlyList<ClientInfoReference> Presets,
    bool PresetsOnly,
    IReadOnlyList<long> ProxyIds,
    bool RequireUpdate);

public sealed record ClientInfoReference(long Id, string Name);

public sealed record ClientStatisticsInfo(bool CanRead, bool? IsEnabled, bool? IsRunning, string? Fault);

public sealed record ClientAccessStatistics(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset BindingSince,
    bool IsEnabled,
    bool IsRunning,
    string? Fault,
    AccessSummary Summary);
