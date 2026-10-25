using AmneziaGeo.Server.Core.Panel;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Reads and saves the settings of the releases of the client.
/// </summary>
public sealed class ClientUpdateStore
{
    private const long Row = 1;

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public ClientUpdateStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the settings the releases are served with, the ones they start with when there are none.
    /// </summary>
    public async Task<ClientUpdateSettings> ReadAsync(CancellationToken ct)
    {
        var held = await _db.ClientUpdates.AsNoTracking().FirstOrDefaultAsync(row => row.Id == Row, ct).ConfigureAwait(false);

        return held is null ? ClientUpdateDefaults.Settings : Read(held);
    }

    /// <summary>
    /// Saves the settings of the releases.
    /// </summary>
    public async Task<ClientUpdateSettings> SaveAsync(ClientUpdateSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var held = await _db.ClientUpdates.FirstOrDefaultAsync(row => row.Id == Row, ct).ConfigureAwait(false);
        if (held is null)
        {
            held = new ClientUpdateEntity { Id = Row };
            _db.Add(held);
        }

        Write(held, settings);
        held.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Read(held);
    }

    private static ClientUpdateSettings Read(ClientUpdateEntity row) => new()
    {
        IsEnabled = row.IsEnabled,
        Source = row.Source.Length > 0 ? row.Source : ClientUpdateDefaults.Own,
        Address = row.Address,
        Token = row.Token,
        Directory = row.Directory,
        Port = row.Port,
        Path = row.Path,
        Default = row.IsDefault,
    };

    private static void Write(ClientUpdateEntity row, ClientUpdateSettings settings)
    {
        row.IsEnabled = settings.IsEnabled;
        row.Source = settings.Source;
        row.Address = settings.Address;
        row.Token = settings.Token;
        row.Directory = settings.Directory;
        row.Port = settings.Port;
        row.Path = settings.Path;
        row.IsDefault = settings.Default;
    }
}
