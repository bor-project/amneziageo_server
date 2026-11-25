using AmneziaGeo.Server.Dal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace AmneziaGeo.Server.Tests;

internal sealed class LegacyDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>
    /// ctor
    /// </summary>
    private LegacyDatabase()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"legacy-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
    }

    public AppDbContext Db { get; }
    public string DatabasePath { get; }

    public static async Task<LegacyDatabase> FromAsync(AppDbContext source, string target)
    {
        var legacy = new LegacyDatabase();
        var ready = false;
        try
        {
            await legacy.Db.Database.OpenConnectionAsync().ConfigureAwait(false);
            await legacy.MigrateAsync(target).ConfigureAwait(false);
            await legacy.CopyAsync(source.Database.GetDbConnection().DataSource).ConfigureAwait(false);
            ready = true;
            return legacy;
        }
        finally
        {
            if (!ready)
            {
                await legacy.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task MigrateAsync(string? target = null)
    {
        await Db.GetService<IMigrator>().MigrateAsync(target).ConfigureAwait(false);
        await IntegrityAsync().ConfigureAwait(false);
    }

    private async Task CopyAsync(string source)
    {
        await Db.Database.ExecuteSqlRawAsync("ATTACH DATABASE {0} AS fixture", source).ConfigureAwait(false);
        try
        {
            Assert.Empty(await ReadAsync("PRAGMA fixture.foreign_key_check").ConfigureAwait(false));
            var tables = await ReadAsync("""
                SELECT name FROM main.sqlite_master
                WHERE type = 'table' AND name NOT GLOB 'sqlite_*'
                  AND name NOT IN ('__EFMigrationsHistory', '__EFMigrationsLock')
                  AND name IN (SELECT name FROM fixture.sqlite_master WHERE type = 'table')
                ORDER BY name
                """).ConfigureAwait(false);
            await Db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF").ConfigureAwait(false);
            try
            {
                var transaction = await Db.Database.BeginTransactionAsync().ConfigureAwait(false);
                await using (transaction.ConfigureAwait(false))
                {
                    foreach (var row in tables)
                    {
                        var table = Quote((string)row[0]);
                        var oldColumns = await ReadAsync($"PRAGMA main.table_info({table})").ConfigureAwait(false);
                        var newColumns = (await ReadAsync($"PRAGMA fixture.table_info({table})").ConfigureAwait(false))
                            .Select(column => (string)column[1]).ToHashSet(StringComparer.Ordinal);
                        var columns = string.Join(',', oldColumns.Select(column => (string)column[1]).Where(newColumns.Contains).Select(Quote));
                        Assert.NotEmpty(columns);
                        await Db.Database.ExecuteSqlRawAsync($"DELETE FROM main.{table}").ConfigureAwait(false);
                        await Db.Database.ExecuteSqlRawAsync($"INSERT INTO main.{table} ({columns}) SELECT {columns} FROM fixture.{table}").ConfigureAwait(false);
                    }
                    await transaction.CommitAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await Db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON").ConfigureAwait(false);
            }
        }
        finally
        {
            await Db.Database.ExecuteSqlRawAsync("DETACH DATABASE fixture").ConfigureAwait(false);
        }
        await IntegrityAsync().ConfigureAwait(false);
    }

    private async Task IntegrityAsync()
    {
        Assert.Equal("ok", (await ReadAsync("PRAGMA integrity_check").ConfigureAwait(false))[0][0]);
        Assert.Empty(await ReadAsync("PRAGMA foreign_key_check").ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<object[]>> ReadAsync(string sql)
    {
        var command = _connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            command.Transaction = (SqliteTransaction?)Db.Database.CurrentTransaction?.GetDbTransaction();
            var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                var rows = new List<object[]>();
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var row = new object[reader.FieldCount];
                    reader.GetValues(row);
                    rows.Add(row);
                }
                return rows;
            }
        }
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = DatabasePath + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
