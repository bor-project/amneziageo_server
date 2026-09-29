using AmneziaGeo.Server.Core.Diagnostics;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps the records of the journal of the panel in a database of its own beside the one of the panel.
/// </summary>
public sealed class JournalRecords
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS Records (
            Id INTEGER PRIMARY KEY,
            At INTEGER NOT NULL,
            Level TEXT NOT NULL,
            Category TEXT NOT NULL,
            Message TEXT NOT NULL,
            Fault TEXT NOT NULL
        );
        """;

    private const int Rounds = 20;

    private readonly string _connection;

    /// <summary>
    /// ctor
    /// </summary>
    public JournalRecords(string location)
    {
        ArgumentNullException.ThrowIfNull(location);

        Location = location;
        _connection = new SqliteConnectionStringBuilder
        {
            DataSource = location,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    /// <summary>
    /// The file the journal is kept in.
    /// </summary>
    public string Location { get; }

    /// <summary>
    /// Returns the file of the journal beside the database of the panel.
    /// </summary>
    public static string PathNear(string database)
    {
        ArgumentNullException.ThrowIfNull(database);

        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database)) ?? ".", JournalDefaults.FileName);
    }

    /// <summary>
    /// Creates the file and the table of the journal when they are not there yet.
    /// </summary>
    public void Prepare()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(Location));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = Open();
        Run(connection, "PRAGMA auto_vacuum = INCREMENTAL");
        Run(connection, "PRAGMA journal_mode = WAL");
        Run(connection, Schema);
    }

    /// <summary>
    /// Adds records to the journal in one step.
    /// </summary>
    public void Write(IReadOnlyList<JournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return;
        }

        using var connection = Open();
        Run(connection, "PRAGMA synchronous = NORMAL");
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO Records (At, Level, Category, Message, Fault) VALUES ($at, $level, $category, $message, $fault)";
        var at = command.Parameters.Add("$at", SqliteType.Integer);
        var level = command.Parameters.Add("$level", SqliteType.Text);
        var category = command.Parameters.Add("$category", SqliteType.Text);
        var message = command.Parameters.Add("$message", SqliteType.Text);
        var fault = command.Parameters.Add("$fault", SqliteType.Text);
        command.Prepare();

        foreach (var entry in entries)
        {
            at.Value = entry.Time.ToUnixTimeMilliseconds();
            level.Value = entry.Level;
            category.Value = entry.Category;
            message.Value = entry.Message;
            fault.Value = entry.Fault;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Returns the latest records, the newest first.
    /// </summary>
    public IReadOnlyList<JournalEntry> Latest(int count)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT At, Level, Category, Message, Fault FROM Records ORDER BY Id DESC LIMIT $count";
        command.Parameters.AddWithValue("$count", Math.Max(count, 0));

        var entries = new List<JournalEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new JournalEntry(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return entries;
    }

    /// <summary>
    /// Returns how many records the journal holds.
    /// </summary>
    public long Count()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Records";

        return (long)(command.ExecuteScalar() ?? 0L);
    }

    /// <summary>
    /// Returns how many bytes the file and its write-ahead log take.
    /// </summary>
    public long Bytes() => new[] { Location, Location + "-wal" }.Where(File.Exists).Sum(file => new FileInfo(file).Length);

    /// <summary>
    /// Removes the oldest records past a count and past a size of the file, returning how many went.
    /// </summary>
    public long Trim(long most, long bytes)
    {
        using var connection = Open();
        var removed = 0L;
        var held = Held(connection);
        if (held > most)
        {
            removed += Drop(connection, held - Kept(most));
            Settle(connection);
        }

        if (Bytes() > bytes)
        {
            Settle(connection);
        }

        for (var round = 0; round < Rounds && Bytes() > bytes; round++)
        {
            held = Held(connection);
            if (held == 0)
            {
                break;
            }

            var before = Bytes();
            removed += Drop(connection, Math.Max(held - Kept(held), 1));
            Settle(connection);
            if (Bytes() >= before)
            {
                break;
            }
        }

        return removed;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connection);
        connection.Open();

        return connection;
    }

    private static void Run(SqliteConnection connection, string text)
    {
        using var command = connection.CreateCommand();
        command.CommandText = text;
        command.ExecuteNonQuery();
    }

    private static long Kept(long count) => count - (count * JournalDefaults.Slack / 100);

    private static long Held(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT IFNULL(MAX(Id) - MIN(Id) + 1, 0) FROM Records";

        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static long Drop(SqliteConnection connection, long count)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Records WHERE Id < (SELECT MIN(Id) FROM Records) + $count";
        command.Parameters.AddWithValue("$count", count);

        return command.ExecuteNonQuery();
    }

    // Gives the free pages back to the host and folds the write-ahead log into the file.
    private static void Settle(SqliteConnection connection)
    {
        Run(connection, "PRAGMA incremental_vacuum");
        Run(connection, "PRAGMA wal_checkpoint(TRUNCATE)");
    }
}
