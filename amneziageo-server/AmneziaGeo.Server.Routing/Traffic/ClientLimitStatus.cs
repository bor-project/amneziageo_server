using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Routing.Traffic;

public sealed record ClientLimitStatus(
    TrafficLimits Policy,
    bool Inherited,
    long? TemplateId,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndsUtc,
    ulong RxBytes,
    ulong TxBytes,
    ulong UsedBytes,
    ulong? RemainingBytes,
    bool IsExhausted,
    bool IsThrottled,
    bool IsBlocked,
    long UploadBitsPerSecond,
    long DownloadBitsPerSecond,
    bool Applied,
    string Error);
