using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Geo.Files;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What managing a geo source produced.
/// </summary>
public enum GeoOutcome
{
    Ok = 0,
    Invalid = 1,
    NameTaken = 2,
    Unknown = 3,
    UrlTaken = 4,
}

/// <summary>
/// The geo source a command produced, and why it was refused.
/// </summary>
public sealed record GeoResult(GeoOutcome Outcome, string Code, string Message, GeoSource? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == GeoOutcome.Ok;

    /// <summary>
    /// Returns the source a command produced.
    /// </summary>
    public static GeoResult Done(GeoSource source) => new(GeoOutcome.Ok, string.Empty, string.Empty, source);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static GeoResult No(GeoOutcome outcome, string code, string message) => new(outcome, code, message, null);
}

/// <summary>
/// Adds, changes, orders and removes the geo sources the panel holds.
/// </summary>
public sealed class GeoStore
{
    private readonly AppDbContext _db;

    private readonly IGeoFileStore _files;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public GeoStore(AppDbContext db, IGeoFileStore files, TimeProvider? time = null)
    {
        _db = db;
        _files = files;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every source the panel holds, in the order they override each other in.
    /// </summary>
    public async Task<IReadOnlyList<GeoSource>> ListAsync(CancellationToken ct)
    {
        var found = await _db.GeoSources.AsNoTracking().OrderBy(source => source.Position).ToListAsync(ct).ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns one source, or null when the panel holds none under the number.
    /// </summary>
    public async Task<GeoSource?> FindAsync(long id, CancellationToken ct)
    {
        var found = await _db.GeoSources.AsNoTracking().FirstOrDefaultAsync(source => source.Id == id, ct).ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Adds the sources a fresh install starts with, once.
    /// </summary>
    public async Task<int> SeedAsync(CancellationToken ct)
    {
        if (await _db.GeoSources.AnyAsync(ct).ConfigureAwait(false))
        {
            return 0;
        }

        var now = _time.GetUtcNow();
        foreach (var source in GeoDefaults.Sources)
        {
            var entity = new GeoSourceEntity { CreatedUtc = now };
            Write(entity, source);
            entity.Position = source.Position;
            _db.GeoSources.Add(entity);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return GeoDefaults.Sources.Length;
    }

    /// <summary>
    /// Adds a source, placing it after the ones already held.
    /// </summary>
    public async Task<GeoResult> AddAsync(GeoSource draft, CancellationToken ct)
    {
        if (await CheckAsync(draft, 0, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        var last = await _db.GeoSources.AnyAsync(ct).ConfigureAwait(false)
            ? await _db.GeoSources.MaxAsync(source => source.Position, ct).ConfigureAwait(false)
            : 0;

        var entity = new GeoSourceEntity { CreatedUtc = _time.GetUtcNow(), Position = last + 1 };
        Write(entity, draft);

        _db.GeoSources.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return GeoResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the settings of a source, dropping the stored file when the address changed.
    /// </summary>
    public async Task<GeoResult> ChangeAsync(long id, GeoSource draft, CancellationToken ct)
    {
        var entity = await _db.GeoSources.FirstOrDefaultAsync(source => source.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        if (await CheckAsync(draft, id, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        var moved = !string.Equals(entity.Url, draft.Url.Trim(), StringComparison.OrdinalIgnoreCase);
        var renamed = !string.Equals(entity.Name, draft.Name.Trim(), StringComparison.Ordinal);
        if (moved || renamed)
        {
            _files.Remove(entity.Name);
            Forget(entity);
        }

        Write(entity, draft);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return GeoResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes a source together with its file.
    /// </summary>
    public async Task<GeoResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.GeoSources.FirstOrDefaultAsync(source => source.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var gone = Read(entity);
        _files.Remove(entity.Name);
        _db.GeoSources.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return GeoResult.Done(gone);
    }

    /// <summary>
    /// Moves a source one place up or down the order.
    /// </summary>
    public async Task<GeoResult> MoveAsync(long id, bool up, CancellationToken ct)
    {
        var entity = await _db.GeoSources.FirstOrDefaultAsync(source => source.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var neighbour = up
            ? await _db.GeoSources.Where(source => source.Position < entity.Position)
                .OrderByDescending(source => source.Position).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : await _db.GeoSources.Where(source => source.Position > entity.Position)
                .OrderBy(source => source.Position).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (neighbour is not null)
        {
            (entity.Position, neighbour.Position) = (neighbour.Position, entity.Position);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return GeoResult.Done(Read(entity));
    }

    /// <summary>
    /// Records what a download left on a source.
    /// </summary>
    public async Task<GeoResult> StampAsync(long id, GeoDownload download, CancellationToken ct)
    {
        var entity = await _db.GeoSources.FirstOrDefaultAsync(source => source.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        entity.UpdatedUtc = download.AtUtc;
        entity.Sha256 = download.Sha256;
        entity.EntryCount = download.EntryCount;
        entity.Size = download.Size;
        entity.ETag = download.ETag;
        entity.LastModified = download.LastModified;
        entity.LastError = string.Empty;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return GeoResult.Done(Read(entity));
    }

    /// <summary>
    /// Records why a download of a source did not go through.
    /// </summary>
    public async Task<GeoResult> FailAsync(long id, string reason, CancellationToken ct)
    {
        var entity = await _db.GeoSources.FirstOrDefaultAsync(source => source.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        entity.LastError = reason;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return GeoResult.Done(Read(entity));
    }

    private async Task<GeoResult?> CheckAsync(GeoSource draft, long id, CancellationToken ct)
    {
        if (GeoSourceRules.Check(draft) is { } fault)
        {
            return GeoResult.No(GeoOutcome.Invalid, fault.Code, fault.Message);
        }

        var name = draft.Name.Trim();
        if (await _db.GeoSources.AnyAsync(source => source.Name == name && source.Id != id, ct).ConfigureAwait(false))
        {
            return GeoResult.No(GeoOutcome.NameTaken, "name-taken", $"the panel already carries a source called '{name}'");
        }

        var url = draft.Url.Trim();
        if (await _db.GeoSources.AnyAsync(source => source.Url == url && source.Id != id, ct).ConfigureAwait(false))
        {
            return GeoResult.No(GeoOutcome.UrlTaken, "url-taken", $"the panel already downloads '{url}'");
        }

        return null;
    }

    private static GeoResult Missing(long id) =>
        GeoResult.No(GeoOutcome.Unknown, "unknown-source", $"there is no geo source under the number {id}");

    private static void Forget(GeoSourceEntity entity)
    {
        entity.UpdatedUtc = null;
        entity.Sha256 = string.Empty;
        entity.EntryCount = 0;
        entity.Size = 0;
        entity.ETag = string.Empty;
        entity.LastModified = string.Empty;
        entity.LastError = string.Empty;
    }

    private static GeoSource Read(GeoSourceEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Kind = entity.Kind,
        Url = entity.Url,
        Position = entity.Position,
        IsEnabled = entity.IsEnabled,
        UpdatedUtc = entity.UpdatedUtc,
        Sha256 = entity.Sha256,
        EntryCount = entity.EntryCount,
        Size = entity.Size,
        ETag = entity.ETag,
        LastModified = entity.LastModified,
        LastError = entity.LastError,
    };

    private static void Write(GeoSourceEntity entity, GeoSource source)
    {
        entity.Name = source.Name.Trim();
        entity.Kind = source.Kind.ToLowerInvariant();
        entity.Url = source.Url.Trim();
        entity.IsEnabled = source.IsEnabled;
    }
}
