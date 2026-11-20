using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

public sealed class TrafficPeriodStore(AppDbContext db)
{
    public async Task<IReadOnlyList<TrafficPeriod>> ReadAsync(CancellationToken ct)
    {
        var rows = await db.TrafficPeriods.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(Read)];
    }

    public async Task SaveAsync(IReadOnlyList<TrafficPeriod> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var ids = rows.Select(row => row.ClientId).Distinct().ToArray();
        var held = (await db.Clients.AsNoTracking().Where(client => ids.Contains(client.Id)).Select(client => client.Id)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var existing = await db.TrafficPeriods.Where(row => ids.Contains(row.ClientId)).ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in rows.Where(row => held.Contains(row.ClientId)))
        {
            var entity = existing.FirstOrDefault(one => one.ClientId == row.ClientId && one.Period == row.Period);
            if (entity is null)
            {
                entity = new TrafficPeriodEntity { ClientId = row.ClientId, Period = row.Period };
                db.TrafficPeriods.Add(entity);
                existing.Add(entity);
            }

            entity.StartedUtc = row.StartedUtc;
            entity.EndsUtc = row.EndsUtc;
            entity.Rx = Signed(row.Used.Rx);
            entity.Tx = Signed(row.Used.Tx);
            entity.SeenRx = Signed(row.Seen.Rx);
            entity.SeenTx = Signed(row.Seen.Tx);
            entity.Generation = row.Generation;
            entity.ObservedUtc = row.ObservedUtc;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static TrafficPeriod Read(TrafficPeriodEntity row) => new(
        row.ClientId, row.Period, row.StartedUtc, row.EndsUtc,
        new ClientUsage(Unsigned(row.Rx), Unsigned(row.Tx)),
        new ClientUsage(Unsigned(row.SeenRx), Unsigned(row.SeenTx)), row.Generation, row.ObservedUtc);

    private static long Signed(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    private static ulong Unsigned(long value) => value < 0 ? 0 : (ulong)value;
}
