using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Tests;

/// <summary>
/// Closes the pooled connections to the databases of one test.
/// </summary>
internal static class Pools
{
    /// <summary>
    /// Closes the pooled connections to the database files given, in every way the server opens one.
    /// </summary>
    public static void Clear(params string[] files)
    {
        foreach (var file in files)
        {
            var ways = new[]
            {
                $"Data Source={file}",
                $"Data Source={file};Mode=ReadOnly",
                new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadWriteCreate }.ToString(),
            };
            foreach (var way in ways)
            {
                using var connection = new SqliteConnection(way);
                SqliteConnection.ClearPool(connection);
            }
        }
    }

    /// <summary>
    /// Closes the pooled connections to every database file under a folder.
    /// </summary>
    public static void ClearUnder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("-wal", StringComparison.Ordinal) && !file.EndsWith("-shm", StringComparison.Ordinal));
        Clear([.. files]);
    }
}
