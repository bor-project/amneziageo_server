using AmneziaGeo.Server.Routing.Access;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Reads and saves the settings of the connection log.
/// </summary>
public sealed class AccessStore
{
    private const long Row = 1;

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public AccessStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the settings the panel holds, the ones it starts with when there are none.
    /// </summary>
    public async Task<AccessSettings> ReadAsync(CancellationToken ct)
    {
        var held = await _db.Access.AsNoTracking().FirstOrDefaultAsync(row => row.Id == Row, ct).ConfigureAwait(false);

        return held is null ? AccessSettings.Default : new AccessSettings { IsEnabled = held.IsEnabled, Days = held.Days };
    }

    /// <summary>
    /// Saves the settings.
    /// </summary>
    public async Task SaveAsync(AccessSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var held = await _db.Access.FirstOrDefaultAsync(row => row.Id == Row, ct).ConfigureAwait(false);
        if (held is null)
        {
            held = new AccessEntity { Id = Row };
            _db.Add(held);
        }

        held.IsEnabled = settings.IsEnabled;
        held.Days = settings.Days;
        held.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
