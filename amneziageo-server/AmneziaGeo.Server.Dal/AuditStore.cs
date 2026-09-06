using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Writes the audit trail into the database.
/// </summary>
public sealed class AuditStore : IAuditLog
{
    private readonly AppDbContext _db;

    /// <summary>
    /// ctor
    /// </summary>
    public AuditStore(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Records one action.
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
        _db.AuditEntries.Add(new AuditEntity
        {
            AtUtc = at,
            UserId = principalId,
            Scheme = scheme,
            Action = action,
            Target = target,
            Detail = detail,
            Address = address,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
