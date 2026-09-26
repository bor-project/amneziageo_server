using System.Data;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps the other writers of the database out while a check and the writes it leads to run.
/// </summary>
internal static class DatabaseLock
{
    /// <summary>
    /// Runs the work in a transaction that takes the write lock of the database at its start, or in the one already open.
    /// </summary>
    public static async Task<T> AloneAsync<T>(this AppDbContext db, Func<Task<T>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await work().ConfigureAwait(false);
        }

        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var result = await work().ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>
    /// Runs the work in a transaction that takes the write lock of the database at its start, or in the one already open.
    /// </summary>
    public static Task AloneAsync(this AppDbContext db, Func<Task> work, CancellationToken ct) =>
        db.AloneAsync(
            async () =>
            {
                await work().ConfigureAwait(false);

                return true;
            },
            ct);
}
