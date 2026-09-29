using AmneziaGeo.Server.Auth;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Writes the audit trail into the database and keeps its latest records alone.
/// </summary>
public sealed class AuditStore : IAuditLog
{
    /// <summary>
    /// How many records the audit trail keeps at most.
    /// </summary>
    public const int MostEntries = 10_000;

    private readonly AppDbContext _db;

    private readonly int _most;

    /// <summary>
    /// ctor
    /// </summary>
    public AuditStore(AppDbContext db, int most = MostEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(most, 1);

        _db = db;
        _most = most;
    }

    /// <summary>
    /// Records one action and lets the oldest records past the limit go.
    /// </summary>
    public async Task WriteAsync(
        DateTimeOffset at,
        long? principalId,
        AuthScheme scheme,
        string action,
        string? target,
        string? detail,
        string? address,
        CancellationToken ct)
    {
        var entry = new AuditEntity
        {
            AtUtc = at,
            UserId = principalId,
            Scheme = scheme,
            Action = action,
            Target = target,
            Detail = detail,
            Address = address,
        };
        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var edge = entry.Id - _most;
        if (edge > 0)
        {
            await _db.AuditEntries.Where(held => held.Id <= edge).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
    }
}
