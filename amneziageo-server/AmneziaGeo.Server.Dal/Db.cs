using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// The database file and the connections to it.
/// </summary>
public sealed class Db
{
    /// <summary>
    /// Environment variable that moves the database off its default path.
    /// </summary>
    public const string PathVariable = "AMNEZIAGEO_DB";

    private const string DefaultFile = "/var/lib/amneziageo-server/server.db";

    private readonly string _connectionString;

    /// <summary>
    /// ctor
    /// </summary>
    public Db(string path)
    {
        Path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    /// <summary>
    /// Path of the database file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Returns the path the server keeps its database at.
    /// </summary>
    public static string DefaultPath() =>
        Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } set ? set : DefaultFile;

    /// <summary>
    /// Opens the database at the default path.
    /// </summary>
    public static Db Default() => new(DefaultPath());

    /// <summary>
    /// Opens a connection with the pragmas the server works by.
    /// </summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Prepare(connection);

        return connection;
    }

    /// <summary>
    /// Opens a connection with the pragmas the server works by.
    /// </summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        Prepare(connection);

        return connection;
    }

    /// <summary>
    /// Creates the directory of the database and brings its schema up to date.
    /// </summary>
    public int Migrate()
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = Open();

        return new SchemaMigrator().Apply(connection);
    }

    private static void Prepare(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
    }
}
