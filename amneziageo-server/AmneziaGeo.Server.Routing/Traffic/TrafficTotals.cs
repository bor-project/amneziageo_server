using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Routing.Traffic;

public sealed record TrafficTotal(
    ulong RxBytes,
    ulong TxBytes,
    ulong TotalBytes,
    DateOnly? SinceDay,
    DateTimeOffset? ResetUtc);

public sealed record TrafficHistory(
    long ClientId,
    ClientUsage Used,
    DateOnly? SinceDay,
    ClientUsage Reset,
    DateTimeOffset? ResetUtc)
{
    public TrafficHistory Add(ClientUsage added, DateOnly day) => this with
    {
        Used = new ClientUsage(Sum(Used.Rx, added.Rx), Sum(Used.Tx, added.Tx)),
        SinceDay = SinceDay is { } first && first < day ? first : day,
    };

    public TrafficTotal Total()
    {
        var rx = Used.Rx - Math.Min(Used.Rx, Reset.Rx);
        var tx = Used.Tx - Math.Min(Used.Tx, Reset.Tx);
        return new TrafficTotal(rx, tx, Sum(rx, tx), SinceDay, ResetUtc);
    }

    private static ulong Sum(ulong first, ulong second) =>
        ulong.MaxValue - first < second ? ulong.MaxValue : first + second;
}
