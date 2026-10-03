using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Copies the whole server database while the server keeps working, and takes in a backup to put in its place.
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
        var target = Fresh();
        try
        {
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

    /// <summary>
    /// Writes a backup the server is handed into a new file next to the database, open to its owner only, and returns
    /// the file's path.
    /// </summary>
    public async Task<string> ReceiveAsync(Stream source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);

        var target = Fresh();
        try
        {
            var file = new FileStream(target, FileMode.Truncate, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await using (file.ConfigureAwait(false))
            {
                await source.CopyToAsync(file, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            File.Delete(target);
            throw;
        }

        return target;
    }

    /// <summary>
    /// Leaves a backup that passed its checks beside the database, where the server puts it in place once it starts
    /// over.
    /// </summary>
    public void Stage(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        DatabaseRestore.Stage(file, Source());
    }

    // Returns the path of the database.
    private string Source() =>
        Path.GetFullPath(new SqliteConnectionStringBuilder(_db.Database.GetConnectionString() ?? string.Empty).DataSource);

    // Makes an empty file next to the database, open to its owner only, and returns its path.
    private string Fresh()
    {
        var target = Path.Combine(Path.GetDirectoryName(Source()) ?? Path.GetTempPath(), $"backup-{Guid.NewGuid():N}.db");

        File.WriteAllBytes(target, []);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            File.Delete(target);
            throw;
        }

        return target;
    }
}
