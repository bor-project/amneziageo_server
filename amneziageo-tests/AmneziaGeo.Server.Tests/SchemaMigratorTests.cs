using AmneziaGeo.Server.Dal;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AmneziaGeo.Server.Tests;

public class SchemaMigratorTests
{
    [Fact]
    public void ApplyRaisesAnEmptyDatabaseToTheAvailableLevel()
    {
        using var connection = Open();
        var migrator = new SchemaMigrator();

        var level = migrator.Apply(connection);

        Assert.Equal(migrator.Available, level);
        Assert.True(level > 0);
        Assert.Equal(level, SchemaMigrator.Level(connection));
    }

    [Fact]
    public void ApplyLeavesAnUpToDateDatabaseAlone()
    {
        using var connection = Open();
        var migrator = new SchemaMigrator();
        migrator.Apply(connection);
        Insert(connection, "keeper");

        var level = migrator.Apply(connection);

        Assert.Equal(migrator.Available, level);
        Assert.Equal(1, Count(connection, "principal"));
    }

    [Fact]
    public void TheAuthLevelCarriesTheTablesEverySchemeNeeds()
    {
        using var connection = Open();
        new SchemaMigrator().Apply(connection);

        foreach (var table in new[]
                 {
                     "principal", "principal_scope", "oauth_identity", "client_certificate",
                     "api_token", "session", "refresh_token", "audit"
                 })
        {
            Assert.True(Exists(connection, table), $"the table '{table}' is missing");
        }
    }

    [Fact]
    public void ScopesFallWithTheirPrincipal()
    {
        using var connection = Open();
        new SchemaMigrator().Apply(connection);
        Insert(connection, "leaver");

        using (var scope = connection.CreateCommand())
        {
            scope.CommandText = "INSERT INTO principal_scope (principal_id, scope) VALUES (1, 'state:read');";
            scope.ExecuteNonQuery();
        }

        using (var drop = connection.CreateCommand())
        {
            drop.CommandText = "DELETE FROM principal WHERE id = 1;";
            drop.ExecuteNonQuery();
        }

        Assert.Equal(0, Count(connection, "principal_scope"));
    }

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static void Insert(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO principal (id, name, created_utc) VALUES (1, $name, '2026-01-01T00:00:00Z');";
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
    }

    private static int Count(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool Exists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }
}
