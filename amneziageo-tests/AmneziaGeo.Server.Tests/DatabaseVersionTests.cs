using AmneziaGeo.Server.Dal;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Tests;

public class DatabaseVersionTests
{
    [Fact]
    public void AWriteOfAnotherConnectionMovesTheVersionAndNothingElseDoes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"amneziageo-version-{Guid.NewGuid():N}.db");
        try
        {
            Write(path, "create table one (id integer primary key)");
            using var version = new DatabaseVersion(path);

            var first = version.Read();
            var still = version.Read();
            Write(path, "insert into one (id) values (1)");
            var moved = version.Read();

            Assert.NotNull(first);
            Assert.Equal(first, still);
            Assert.NotEqual(first, moved);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ADatabaseThatIsNotThereHasNoVersion()
    {
        using var version = new DatabaseVersion(Path.Combine(Path.GetTempPath(), $"amneziageo-none-{Guid.NewGuid():N}.db"));

        Assert.Null(version.Read());
    }

    private static void Write(string path, string text)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = text;
        command.ExecuteNonQuery();
    }
}
