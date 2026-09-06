using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Applies numbered schema scripts to a database and records the level in user_version.
/// </summary>
public sealed partial class SchemaMigrator
{
    private readonly Assembly _assembly;

    /// <summary>
    /// ctor
    /// </summary>
    public SchemaMigrator(Assembly? assembly = null)
    {
        _assembly = assembly ?? typeof(SchemaMigrator).Assembly;
    }

    /// <summary>
    /// The highest level the embedded scripts reach.
    /// </summary>
    public int Available => Scripts().Count == 0 ? 0 : Scripts()[^1].Level;

    /// <summary>
    /// Brings a database up to the highest available level and returns that level.
    /// </summary>
    public int Apply(SqliteConnection connection)
    {
        Prepare(connection);

        var level = Level(connection);
        foreach (var script in Scripts())
        {
            if (script.Level <= level)
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Text(script.Resource);
                command.ExecuteNonQuery();
            }

            using (var stamp = connection.CreateCommand())
            {
                stamp.Transaction = transaction;
                stamp.CommandText = $"PRAGMA user_version = {script.Level};";
                stamp.ExecuteNonQuery();
            }

            transaction.Commit();
            level = script.Level;
        }

        return level;
    }

    /// <summary>
    /// Reads the level a database currently sits at.
    /// </summary>
    public static int Level(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void Prepare(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
    }

    private List<(int Level, string Resource)> Scripts()
    {
        var found = new List<(int Level, string Resource)>();
        foreach (var name in _assembly.GetManifestResourceNames())
        {
            var match = ScriptName().Match(name);
            if (match.Success)
            {
                found.Add((int.Parse(match.Groups[1].Value), name));
            }
        }

        found.Sort((a, b) => a.Level.CompareTo(b.Level));
        return found;
    }

    private string Text(string resource)
    {
        using var stream = _assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"the schema script '{resource}' is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\.Schema\.V(\d+)_[^.]+\.sql$")]
    private static partial Regex ScriptName();
}
