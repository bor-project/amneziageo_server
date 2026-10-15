using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Tells whether the data changed since a reader last looked, whoever wrote it: the panel or the utility of the host.
/// </summary>
public sealed class DatabaseVersion : IDisposable
{
    private readonly string _path;

    private readonly Lock _sync = new();

    private SqliteConnection? _connection;

    /// <summary>
    /// ctor
    /// </summary>
    public DatabaseVersion(string path) => _path = path;

    /// <summary>
    /// Reads the version of the data, which moves whenever another connection commits a change; null when the
    /// database does not read.
    /// </summary>
    public long? Read()
    {
        lock (_sync)
        {
            try
            {
                _connection ??= Open();
                using var command = _connection.CreateCommand();
                command.CommandText = "PRAGMA data_version";

                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            catch (SqliteException)
            {
                _connection?.Dispose();
                _connection = null;

                return null;
            }
        }
    }

    /// <summary>
    /// Closes the connection.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }

    private SqliteConnection Open()
    {
        var text = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        var connection = new SqliteConnection(text);
        try
        {
            connection.Open();

            return connection;
        }
        catch (SqliteException)
        {
            connection.Dispose();

            throw;
        }
    }
}
