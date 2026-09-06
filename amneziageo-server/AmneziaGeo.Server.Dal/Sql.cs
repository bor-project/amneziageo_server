using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Small helpers the stores share.
/// </summary>
internal static class Sql
{
    /// <summary>
    /// Writes an instant the way the schema stores it.
    /// </summary>
    public static string Text(DateTimeOffset at) => at.UtcDateTime.ToString("O");

    /// <summary>
    /// Reads an instant back, returning null on an empty column.
    /// </summary>
    public static DateTimeOffset? Time(string? text) =>
        string.IsNullOrEmpty(text) ? null : DateTimeOffset.Parse(text, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

    /// <summary>
    /// Returns a command carrying a statement and its parameters.
    /// </summary>
    public static SqliteCommand Command(SqliteConnection connection, string text, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = text;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        return command;
    }

    /// <summary>
    /// Reads a text column, returning null when it is empty.
    /// </summary>
    public static string? TextOrNull(this SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetString(index);
}
