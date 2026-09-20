using System.Net;
using AmneziaGeo.Server.Routing.Dns;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Reads and saves the addresses the rules stand on.
/// </summary>
public sealed class DnsStandingStore
{
    private readonly AppDbContext _db;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsStandingStore(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns the addresses the rules stood on.
    /// </summary>
    public async Task<IReadOnlyList<DnsStanding>> ListAsync(CancellationToken ct)
    {
        var rows = await _db.Standings.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        return [.. rows.Select(Read).OfType<DnsStanding>()];
    }

    /// <summary>
    /// Saves the addresses the rules stand on in place of the ones held.
    /// </summary>
    public async Task SaveAsync(IReadOnlyList<DnsStanding> standings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(standings);

        _db.Standings.RemoveRange(await _db.Standings.ToListAsync(ct).ConfigureAwait(false));
        _db.Standings.AddRange(standings.Select(one => new DnsStandingEntity
        {
            RuleId = one.Rule,
            Address = one.Address.ToString(),
            SeenUtc = one.Seen,
        }));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static DnsStanding? Read(DnsStandingEntity row) =>
        IPAddress.TryParse(row.Address, out var address) ? new DnsStanding(row.RuleId, address, row.SeenUtc) : null;
}
