using System.Net;
using AmneziaGeo.Server.Routing.Dns;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Reads and saves the names the resolver answers with stand-in addresses.
/// </summary>
public sealed class StandInStore
{
    private readonly AppDbContext _db;

    /// <summary>
    /// ctor
    /// </summary>
    public StandInStore(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns the names with their stand-in addresses.
    /// </summary>
    public async Task<IReadOnlyList<DnsStandIn>> ListAsync(CancellationToken ct)
    {
        var rows = await _db.StandIns.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        return [.. rows.Select(Read).OfType<DnsStandIn>()];
    }

    /// <summary>
    /// Saves the names with their stand-in addresses in place of the ones held.
    /// </summary>
    public async Task SaveAsync(IReadOnlyList<DnsStandIn> held, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(held);

        _db.StandIns.RemoveRange(await _db.StandIns.ToListAsync(ct).ConfigureAwait(false));
        _db.StandIns.AddRange(held.Select(one => new StandInEntity
        {
            Name = one.Name,
            Address = one.Address.ToString(),
            Real = one.Real?.ToString() ?? string.Empty,
            SeenUtc = one.Seen,
        }));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static DnsStandIn? Read(StandInEntity row)
    {
        if (!IPAddress.TryParse(row.Address, out var address))
        {
            return null;
        }

        var real = IPAddress.TryParse(row.Real, out var found) ? found : null;

        return new DnsStandIn(row.Name, address, real, row.SeenUtc);
    }
}
