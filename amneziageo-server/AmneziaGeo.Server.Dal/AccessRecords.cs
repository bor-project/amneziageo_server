using System.Text;
using AmneziaGeo.Server.Routing.Access;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps the records of the connection log in a database of its own beside the one of the panel.
/// </summary>
public sealed class AccessRecords
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS Records (
            Id INTEGER PRIMARY KEY,
            At INTEGER NOT NULL,
            Client TEXT NOT NULL,
            Source TEXT NOT NULL,
            SourcePort INTEGER NOT NULL,
            Inbound TEXT NOT NULL,
            Protocol INTEGER NOT NULL,
            Target TEXT NOT NULL,
            Port INTEGER NOT NULL,
            Name TEXT NOT NULL,
            Verdict TEXT NOT NULL,
            Rule INTEGER NULL,
            RuleName TEXT NOT NULL,
            Way TEXT NOT NULL,
            Via TEXT NOT NULL,
            Path TEXT NOT NULL,
            Outcome TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS RecordsAt ON Records (At);
        """;

    private const string Columns =
        "Id, At, Client, Source, SourcePort, Inbound, Protocol, Target, Port, Name, Verdict, Rule, RuleName, Way, Via, " +
        "Path, Outcome";

    private static readonly string Failures = string.Join(", ", AccessOutcome.Failures.Select(outcome => $"'{outcome}'"));

    private const int Chunk = 10000;

    private readonly string _connection;

    /// <summary>
    /// ctor
    /// </summary>
    public AccessRecords(string location)
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
    /// The file the log is kept in.
    /// </summary>
    public string Location { get; }

    /// <summary>
    /// Returns the file of the log beside the database of the panel.
    /// </summary>
    public static string PathNear(string database)
    {
        ArgumentNullException.ThrowIfNull(database);

        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database)) ?? ".", AccessDefaults.FileName);
    }

    /// <summary>
    /// Creates the file and the table of the log when they are not there yet.
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
    /// Adds records to the log in one step.
    /// </summary>
    public void Write(IReadOnlyList<AccessRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return;
        }

        using var connection = Open();
        Run(connection, "PRAGMA synchronous = NORMAL");
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO Records (At, Client, Source, SourcePort, Inbound, Protocol, Target, Port, Name, Verdict, Rule, " +
            "RuleName, Way, Via, Path, Outcome) VALUES ($at, $client, $source, $sourcePort, $inbound, $protocol, $target, " +
            "$port, $name, $verdict, $rule, $ruleName, $way, $via, $path, $outcome)";
        var at = command.Parameters.Add("$at", SqliteType.Integer);
        var client = command.Parameters.Add("$client", SqliteType.Text);
        var source = command.Parameters.Add("$source", SqliteType.Text);
        var sourcePort = command.Parameters.Add("$sourcePort", SqliteType.Integer);
        var inbound = command.Parameters.Add("$inbound", SqliteType.Text);
        var protocol = command.Parameters.Add("$protocol", SqliteType.Integer);
        var target = command.Parameters.Add("$target", SqliteType.Text);
        var port = command.Parameters.Add("$port", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var verdict = command.Parameters.Add("$verdict", SqliteType.Text);
        var rule = command.Parameters.Add("$rule", SqliteType.Integer);
        var ruleName = command.Parameters.Add("$ruleName", SqliteType.Text);
        var way = command.Parameters.Add("$way", SqliteType.Text);
        var via = command.Parameters.Add("$via", SqliteType.Text);
        var path = command.Parameters.Add("$path", SqliteType.Text);
        var outcome = command.Parameters.Add("$outcome", SqliteType.Text);
        command.Prepare();

        foreach (var record in records)
        {
            at.Value = record.At.ToUnixTimeMilliseconds();
            client.Value = record.Client;
            source.Value = record.Source;
            sourcePort.Value = record.SourcePort;
            inbound.Value = record.Inbound;
            protocol.Value = record.Protocol;
            target.Value = record.Target;
            port.Value = record.Port;
            name.Value = record.Name;
            verdict.Value = record.Verdict;
            rule.Value = record.Rule is { } id ? id : DBNull.Value;
            ruleName.Value = record.RuleName;
            way.Value = record.Way;
            via.Value = record.Via;
            path.Value = record.Path;
            outcome.Value = record.Outcome;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Returns one page of the records a query asks for, the newest first.
    /// </summary>
    public IReadOnlyList<AccessRecord> List(AccessQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = Where(command, query);
        if (query.Before is { } before)
        {
            var edge = Edge(connection, before);
            where.Append(edge is null ? " AND Id < $before" : " AND (At < $edge OR (At = $edge AND Id < $before))");
            command.Parameters.AddWithValue("$before", before);
            command.Parameters.AddWithValue("$edge", edge ?? 0L);
        }

        command.CommandText = $"SELECT {Columns} FROM Records WHERE {where} ORDER BY At DESC, Id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, AccessDefaults.MostPage));

        var records = new List<AccessRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(Record(reader));
        }

        return records;
    }

    /// <summary>
    /// Returns what the records a query asks for came to, put together one way.
    /// </summary>
    public AccessSummary Summary(AccessQuery query, string by)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(by);

        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = Where(command, query);
        var verdicts = Counts(command, $"SELECT Verdict, COUNT(*) FROM Records WHERE {where} GROUP BY Verdict");
        var ways = Counts(
            command,
            $"SELECT Way, COUNT(*) FROM Records WHERE {where} AND Verdict = 'out' GROUP BY Way");
        var paths = Counts(command, $"SELECT Path, COUNT(*) FROM Records WHERE {where} AND Path != '' GROUP BY Path");
        var outcomes = Counts(
            command,
            $"SELECT Outcome, COUNT(*) FROM Records WHERE {where} AND Outcome != '' GROUP BY Outcome");
        var groups = Groups(command, where.ToString(), by);
        var ordered = groups.OrderByDescending(group => group.Count).ThenBy(group => group.Key, StringComparer.Ordinal);

        return new AccessSummary(
            verdicts.Sum(count => count.Count),
            verdicts,
            ways,
            paths,
            outcomes,
            [.. ordered.Take(AccessDefaults.MostGroups)],
            groups.Count > AccessDefaults.MostGroups);
    }

    /// <summary>
    /// Returns how many records the log holds and over what time.
    /// </summary>
    public AccessStock Stock()
    {
        var bytes = new[] { Location, Location + "-wal" }.Where(File.Exists).Sum(file => new FileInfo(file).Length);
        if (!File.Exists(Location))
        {
            return new AccessStock(0, null, null, 0);
        }

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(At), MAX(At) FROM Records";
        using var reader = command.ExecuteReader();
        reader.Read();
        var count = reader.GetInt64(0);

        return count == 0
            ? new AccessStock(0, null, null, bytes)
            : new AccessStock(count, Time(reader.GetInt64(1)), Time(reader.GetInt64(2)), bytes);
    }

    /// <summary>
    /// Removes the records older than a time and the oldest past a count, returning how many went.
    /// </summary>
    public long Trim(DateTimeOffset before, long most)
    {
        using var connection = Open();
        var removed = Chunks(
            connection,
            "DELETE FROM Records WHERE Id IN (SELECT Id FROM Records WHERE At < $before ORDER BY Id LIMIT $chunk)",
            ("$before", before.ToUnixTimeMilliseconds()));

        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Records";
        var over = (long)(count.ExecuteScalar() ?? 0L) - most;
        if (over > 0)
        {
            using var edge = connection.CreateCommand();
            edge.CommandText = "SELECT Id FROM Records ORDER BY Id LIMIT 1 OFFSET $over";
            edge.Parameters.AddWithValue("$over", over);
            if (edge.ExecuteScalar() is long first)
            {
                removed += Chunks(
                    connection,
                    "DELETE FROM Records WHERE Id IN (SELECT Id FROM Records WHERE Id < $first ORDER BY Id LIMIT $chunk)",
                    ("$first", first));
            }
        }

        if (removed > 0)
        {
            Run(connection, "PRAGMA incremental_vacuum");
        }

        return removed;
    }

    /// <summary>
    /// Removes every record and gives the room back to the host.
    /// </summary>
    public void Clear()
    {
        using var connection = Open();
        Run(connection, "DELETE FROM Records");
        Run(connection, "VACUUM");
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

    private static long Chunks(SqliteConnection connection, string text, (string Name, long Value) bound)
    {
        var removed = 0L;
        while (true)
        {
            using var command = connection.CreateCommand();
            command.CommandText = text;
            command.Parameters.AddWithValue(bound.Name, bound.Value);
            command.Parameters.AddWithValue("$chunk", Chunk);
            var gone = command.ExecuteNonQuery();
            removed += gone;
            if (gone < Chunk)
            {
                return removed;
            }
        }
    }

    private static StringBuilder Where(SqliteCommand command, AccessQuery query)
    {
        var where = new StringBuilder("At >= $from AND At < $to");
        command.Parameters.AddWithValue("$from", query.From.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", query.To.ToUnixTimeMilliseconds());

        if (query.Client.Length > 0)
        {
            where.Append(" AND (Client = $client OR Source = $client)");
            command.Parameters.AddWithValue("$client", query.Client);
        }

        if (query.Verdict.Length > 0)
        {
            where.Append(" AND Verdict = $verdict");
            command.Parameters.AddWithValue("$verdict", query.Verdict);
        }

        if (query.Way.Length > 0)
        {
            where.Append(" AND Way = $way");
            command.Parameters.AddWithValue("$way", query.Way);
        }

        if (query.Path.Length > 0)
        {
            where.Append(" AND Path = $path");
            command.Parameters.AddWithValue("$path", query.Path);
        }

        if (query.Outcome == AccessOutcome.Failed)
        {
            where.Append(" AND Outcome IN (").Append(Failures).Append(')');
        }
        else if (query.Outcome.Length > 0)
        {
            where.Append(" AND Outcome = $outcome");
            command.Parameters.AddWithValue("$outcome", query.Outcome);
        }

        if (query.Search.Trim() is { Length: > 0 } search)
        {
            where.Append(
                " AND (Name LIKE $search ESCAPE '\\' OR Target LIKE $search ESCAPE '\\'" +
                " OR RuleName LIKE $search ESCAPE '\\' OR Client LIKE $search ESCAPE '\\')");
            command.Parameters.AddWithValue("$search", "%" + Escaped(search) + "%");
        }

        return where;
    }

    private static long? Edge(SqliteConnection connection, long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT At FROM Records WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        return command.ExecuteScalar() is long at ? at : null;
    }

    private static List<AccessCount> Counts(SqliteCommand command, string text)
    {
        command.CommandText = text;
        var counts = new List<AccessCount>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            counts.Add(new AccessCount(reader.GetString(0), reader.GetInt64(1)));
        }

        return [.. counts.OrderByDescending(count => count.Count)];
    }

    private static List<AccessGroup> Groups(SqliteCommand command, string where, string by)
    {
        const string named = "Name, CASE WHEN Name = '' THEN Target ELSE '' END";
        var (key, grouped, keyed) = by switch
        {
            AccessGrouping.Rule => ("RuleName, ''", "RuleName", false),
            AccessGrouping.Way => ("Way, ''", "Way", false),
            AccessGrouping.Client => ("Client, CASE WHEN Client = '' THEN Source ELSE '' END", "Client", false),
            AccessGrouping.Name => (named, named, false),
            _ => (named, named, true),
        };

        command.CommandText =
            $"SELECT {key}, Verdict, Way, Via, RuleName, Source, COUNT(*), MIN(At), MAX(At), SUM(Outcome = 'ok'), " +
            $"SUM(Outcome IN ({Failures})) FROM Records WHERE {where} " +
            $"GROUP BY {grouped}, Verdict, Way, Via, RuleName, Source";

        var folded = new Dictionary<(string Key, string Verdict, string Way, string Via, string Rule), Fold>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var first = reader.GetString(0);
            var shown = first.Length > 0 ? first : reader.GetString(1);
            var group = (keyed && first.Length > 0 ? AccessDomain.Of(shown) : shown,
                reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
            if (!folded.TryGetValue(group, out var fold))
            {
                fold = new Fold();
                folded[group] = fold;
            }

            fold.Sources.Add(reader.GetString(6));
            fold.Count += reader.GetInt64(7);
            fold.First = Math.Min(fold.First, reader.GetInt64(8));
            fold.Last = Math.Max(fold.Last, reader.GetInt64(9));
            fold.Ok += reader.GetInt64(10);
            fold.Failed += reader.GetInt64(11);
        }

        return
        [
            .. folded.Select(pair => new AccessGroup(
                pair.Key.Key,
                pair.Key.Verdict,
                pair.Key.Way,
                pair.Key.Via,
                pair.Key.Rule,
                pair.Value.Sources.Count,
                pair.Value.Count,
                pair.Value.Ok,
                pair.Value.Failed,
                Time(pair.Value.First),
                Time(pair.Value.Last))),
        ];
    }

    private static AccessRecord Record(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        At = Time(reader.GetInt64(1)),
        Client = reader.GetString(2),
        Source = reader.GetString(3),
        SourcePort = reader.GetInt32(4),
        Inbound = reader.GetString(5),
        Protocol = reader.GetInt32(6),
        Target = reader.GetString(7),
        Port = reader.GetInt32(8),
        Name = reader.GetString(9),
        Verdict = reader.GetString(10),
        Rule = reader.IsDBNull(11) ? null : reader.GetInt64(11),
        RuleName = reader.GetString(12),
        Way = reader.GetString(13),
        Via = reader.GetString(14),
        Path = reader.GetString(15),
        Outcome = reader.GetString(16),
    };

    private static DateTimeOffset Time(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);

    private static string Escaped(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed class Fold
    {
        public HashSet<string> Sources { get; } = new(StringComparer.Ordinal);

        public long Count { get; set; }

        public long First { get; set; } = long.MaxValue;

        public long Last { get; set; } = long.MinValue;

        public long Ok { get; set; }

        public long Failed { get; set; }
    }
}
