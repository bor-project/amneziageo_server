using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using AmneziaGeo.Server.Awg.Config;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

public sealed class ConfigDraftEntity
{
    public long ConfigId { get; set; }
    public string BaseRevision { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed record PendingConfigDraft(long ConfigId, string BaseRevision, string Payload, DateTimeOffset CreatedUtc);

public sealed class ConfigDraftStore(AppDbContext db, TimeProvider time)
{
    public Task<IDbContextTransaction> BeginAsync(CancellationToken ct) => db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

    /// <summary>
    /// Returns the interface revision.
    /// </summary>
    public static string Revision(ServerConfig config) => "v2:" + Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(config with { CreatedUtc = default, UpdatedUtc = default })));

    public static string PayloadRevision(string payload) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    /// <summary>
    /// Returns the pending interface change.
    /// </summary>
    public async Task<PendingConfigDraft?> FindAsync(long id, CancellationToken ct)
    {
        var found = await db.ConfigDrafts.AsNoTracking().SingleOrDefaultAsync(one => one.ConfigId == id, ct).ConfigureAwait(false);
        return found is null ? null : new(found.ConfigId, found.BaseRevision, found.Payload, found.CreatedUtc);
    }

    /// <summary>
    /// Saves a pending interface change.
    /// </summary>
    public async Task SaveAsync(ServerConfig current, string payload, CancellationToken ct)
    {
        var entity = await db.ConfigDrafts.SingleOrDefaultAsync(one => one.ConfigId == current.Id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new ConfigDraftEntity { ConfigId = current.Id };
            db.ConfigDrafts.Add(entity);
        }
        entity.BaseRevision = Revision(current);
        entity.Payload = payload;
        entity.CreatedUtc = time.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a pending interface change.
    /// </summary>
    public async Task RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await db.ConfigDrafts.SingleOrDefaultAsync(one => one.ConfigId == id, ct).ConfigureAwait(false);
        if (entity is not null)
        {
            db.ConfigDrafts.Remove(entity);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
