using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps the traffic the clients made day by day.
/// </summary>
public sealed class TrafficStore
{
    private readonly AppDbContext _db;

    /// <summary>
    /// ctor
    /// </summary>
    public TrafficStore(AppDbContext db) => _db = db;

    /// <summary>
    /// Returns the last day counted for every client, with the counters of its peer read last.
    /// </summary>
    public async Task<IReadOnlyList<TrafficDay>> LatestAsync(CancellationToken ct)
    {
        var found = await _db.Traffic.AsNoTracking()
            .Where(row => !_db.Traffic.Any(other => other.ClientId == row.ClientId && other.Day > row.Day))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Writes the traffic of the days, passing over the clients the panel no longer holds.
    /// </summary>
    public async Task SaveAsync(IReadOnlyList<TrafficDay> days, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(days);

        if (days.Count == 0)
        {
            return;
        }

        var ids = days.Select(day => day.ClientId).Distinct().ToArray();
        var earliest = days.Min(day => day.Day);
        var held = (await _db.Clients.AsNoTracking()
                .Where(client => ids.Contains(client.Id))
                .Select(client => client.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToHashSet();
        var found = await _db.Traffic
            .Where(row => ids.Contains(row.ClientId) && row.Day >= earliest)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var day in days.Where(one => held.Contains(one.ClientId)))
        {
            var entity = found.FirstOrDefault(row => row.ClientId == day.ClientId && row.Day == day.Day);
            if (entity is null)
            {
                entity = new TrafficEntity { ClientId = day.ClientId, Day = day.Day };
                _db.Traffic.Add(entity);
                found.Add(entity);
            }

            entity.Rx = Signed(day.Used.Rx);
            entity.Tx = Signed(day.Used.Tx);
            entity.SeenRx = Signed(day.Seen.Rx);
            entity.SeenTx = Signed(day.Seen.Tx);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TrafficDay>> DaysAsync(long clientId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var found = await _db.Traffic.AsNoTracking()
            .Where(row => row.ClientId == clientId && row.Day >= from && row.Day <= to)
            .OrderBy(row => row.Day)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. found.Select(Read)];
    }

    public async Task<IReadOnlyList<TrafficHistory>> HistoryAsync(CancellationToken ct)
    {
        var clients = await _db.Clients.AsNoTracking()
            .Select(one => new { one.Id, one.TrafficResetRx, one.TrafficResetTx, one.TrafficResetUtc })
            .ToListAsync(ct).ConfigureAwait(false);
        var totals = clients.ToDictionary(one => one.Id, one => new TrafficHistory(one.Id, default, null,
            new ClientUsage(one.TrafficResetRx, one.TrafficResetTx), one.TrafficResetUtc));
        await foreach (var row in _db.Traffic.AsNoTracking().AsAsyncEnumerable().WithCancellation(ct).ConfigureAwait(false))
        {
            if (totals.TryGetValue(row.ClientId, out var held))
            {
                totals[row.ClientId] = held.Add(new ClientUsage(Unsigned(row.Rx), Unsigned(row.Tx)), row.Day);
            }
        }

        return [.. totals.Values];
    }

    public async Task<bool> ResetAsync(long clientId, ClientUsage baseline, DateTimeOffset now, CancellationToken ct)
    {
        var client = await _db.Clients.FindAsync([clientId], ct).ConfigureAwait(false);
        if (client is null)
        {
            return false;
        }

        client.TrafficResetRx = baseline.Rx;
        client.TrafficResetTx = baseline.Tx;
        client.TrafficResetUtc = now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static TrafficDay Read(TrafficEntity entity) => new(
        entity.ClientId,
        entity.Day,
        new ClientUsage(Unsigned(entity.Rx), Unsigned(entity.Tx)),
        new ClientUsage(Unsigned(entity.SeenRx), Unsigned(entity.SeenTx)));

    private static long Signed(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    private static ulong Unsigned(long value) => value < 0 ? 0 : (ulong)value;
}
