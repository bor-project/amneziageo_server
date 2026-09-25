using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Tells whether a file is a sound database of the panel this release takes.
/// </summary>
public static class DatabaseCheck
{
    /// <summary>
    /// The answer for a sound database of the panel.
    /// </summary>
    public const string Sound = "ok";

    private static readonly string[] Tables = ["__EFMigrationsHistory", "Panel", "Configs", "AspNetUsers"];

    /// <summary>
    /// Returns the newest migration of the database this release carries.
    /// </summary>
    public static string Newest() =>
        typeof(AppDbContext).Assembly.GetTypes()
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
            .OfType<string>()
            .Max(StringComparer.Ordinal) ?? string.Empty;

    /// <summary>
    /// Returns ok for a sound database of the panel, or what is wrong with it; a database of a newer release is turned down.
    /// </summary>
    public static string Inspect(string path, string newest)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(newest);

        if (!File.Exists(path))
        {
            return "there is no such file";
        }

        if (!Headed(path))
        {
            return "not a database";
        }

        try
        {
            var source = new SqliteConnectionStringBuilder
            {
                DataSource = new Uri(Path.GetFullPath(path)).AbsoluteUri + "?immutable=1",
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            using var connection = new SqliteConnection(source.ToString());
            connection.Open();

            using var names = connection.CreateCommand();
            names.CommandText = "select name from sqlite_master where type = 'table'";
            var tables = Column(names);
            if (!Tables.All(tables.Contains))
            {
                return "not a database of the panel";
            }

            using var integrity = connection.CreateCommand();
            integrity.CommandText = "pragma integrity_check";
            if (Column(integrity).FirstOrDefault() != "ok")
            {
                return "the database is damaged";
            }

            using var migrations = connection.CreateCommand();
            migrations.CommandText = "select max(MigrationId) from __EFMigrationsHistory";
            var mine = Column(migrations).FirstOrDefault() ?? string.Empty;

            return string.CompareOrdinal(mine, newest) > 0
                ? "the backup comes from a newer release of the panel, update the panel first"
                : Sound;
        }
        catch (SqliteException)
        {
            return "the database is damaged";
        }
    }

    private static bool Headed(string path)
    {
        var expected = "SQLite format 3\0"u8;
        using var file = File.OpenRead(path);
        Span<byte> head = stackalloc byte[16];

        return file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length && head.SequenceEqual(expected);
    }

    private static List<string> Column(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }

        return values;
    }
}
