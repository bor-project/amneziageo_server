using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Copies the whole server database while the server keeps working.
/// </summary>
public sealed class DatabaseBackup
{
    private readonly AppDbContext _db;

    /// <summary>
    /// ctor
    /// </summary>
    public DatabaseBackup(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Writes a copy of the database into a new file next to it, open to its owner only, and returns the file's path.
    /// </summary>
    public async Task<string> CopyAsync(CancellationToken ct)
    {
        var connection = _db.Database.GetConnectionString() ?? string.Empty;
        var source = Path.GetFullPath(new SqliteConnectionStringBuilder(connection).DataSource);
        var target = Path.Combine(Path.GetDirectoryName(source) ?? Path.GetTempPath(), $"backup-{Guid.NewGuid():N}.db");

        File.WriteAllBytes(target, []);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            using var from = new SqliteConnection(connection);
            await from.OpenAsync(ct).ConfigureAwait(false);
            using var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
            await to.OpenAsync(ct).ConfigureAwait(false);
            from.BackupDatabase(to);
        }
        catch
        {
            File.Delete(target);
            throw;
        }

        return target;
    }
}
