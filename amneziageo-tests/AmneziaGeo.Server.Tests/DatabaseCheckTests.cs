using AmneziaGeo.Server.Dal;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Tests;

public sealed class DatabaseCheckTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-check-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task ABackupOfThePanelIsSound()
    {
        using var bench = new Bench();
        var copy = await new DatabaseBackup(bench.Db).CopyAsync(CancellationToken.None);
        try
        {
            Assert.Equal(DatabaseCheck.Sound, DatabaseCheck.Inspect(copy, DatabaseCheck.Newest()));
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public void TheNewestMigrationIsTheLastOneThisReleaseCarries()
    {
        var newest = DatabaseCheck.Newest();

        Assert.Matches("^[0-9]{14}_", newest);
        Assert.True(string.CompareOrdinal(newest, "20260923185655_PanelNameTemplate") >= 0);
    }

    [Fact]
    public void ABackupOfANewerReleaseIsTurnedDown()
    {
        var file = Database("newer.db", "99990101000000_Later");

        Assert.Equal(
            "the backup comes from a newer release of the panel, update the panel first",
            DatabaseCheck.Inspect(file, DatabaseCheck.Newest()));
        Assert.Equal(DatabaseCheck.Sound, DatabaseCheck.Inspect(Database("older.db", "20260901000000_Earlier"), DatabaseCheck.Newest()));
    }

    [Fact]
    public void AFileThatIsNotADatabaseOfThePanelIsTurnedDown()
    {
        var text = Path.Combine(_folder.FullName, "notes.db");
        File.WriteAllText(text, "not a database at all, just some words");
        var other = Path.Combine(_folder.FullName, "other.db");
        using (var connection = new SqliteConnection($"Data Source={other};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "create table Notes (Id integer primary key)";
            command.ExecuteNonQuery();
        }

        Assert.Equal("there is no such file", DatabaseCheck.Inspect(Path.Combine(_folder.FullName, "none.db"), DatabaseCheck.Newest()));
        Assert.Equal("not a database", DatabaseCheck.Inspect(text, DatabaseCheck.Newest()));
        Assert.Equal("not a database of the panel", DatabaseCheck.Inspect(other, DatabaseCheck.Newest()));
    }

    [Fact]
    public void EveryVerdictOnABackupGoesByACodeOfItsOwnWithTheSameWords()
    {
        var text = Path.Combine(_folder.FullName, "notes.db");
        File.WriteAllText(text, "not a database at all, just some words");
        var other = Path.Combine(_folder.FullName, "other.db");
        using (var connection = new SqliteConnection($"Data Source={other};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "create table Notes (Id integer primary key)";
            command.ExecuteNonQuery();
        }

        var damaged = Database("broken.db", "20260901000000_Earlier");
        var bytes = File.ReadAllBytes(damaged);
        Array.Fill(bytes, (byte)0x5a, 4096, bytes.Length - 4096);
        File.WriteAllBytes(damaged, bytes);

        var newest = DatabaseCheck.Newest();
        var sound = DatabaseCheck.Judge(Database("older.db", "20260901000000_Earlier"), newest);

        Assert.Equal(
            new DatabaseVerdict(DatabaseCheck.NotDatabase, "there is no such file"),
            DatabaseCheck.Judge(Path.Combine(_folder.FullName, "none.db"), newest));
        Assert.Equal(new DatabaseVerdict("backup-not-database", "not a database"), DatabaseCheck.Judge(text, newest));
        Assert.Equal(new DatabaseVerdict("backup-foreign", "not a database of the panel"), DatabaseCheck.Judge(other, newest));
        Assert.Equal(new DatabaseVerdict("backup-damaged", "the database is damaged"), DatabaseCheck.Judge(damaged, newest));
        Assert.Equal("backup-newer", DatabaseCheck.Judge(Database("newer.db", "99990101000000_Later"), newest).Code);
        Assert.True(sound.IsSound);
        Assert.Equal(new DatabaseVerdict(string.Empty, DatabaseCheck.Sound), sound);
        Assert.Equal(DatabaseCheck.Judge(other, newest).Message, DatabaseCheck.Inspect(other, newest));
    }

    [Fact]
    public void ADamagedDatabaseIsTurnedDown()
    {
        var file = Database("damaged.db", "20260901000000_Earlier");
        var bytes = File.ReadAllBytes(file);
        for (var at = 4096; at < bytes.Length; at++)
        {
            bytes[at] = 0x5a;
        }

        File.WriteAllBytes(file, bytes);

        Assert.Equal("the database is damaged", DatabaseCheck.Inspect(file, DatabaseCheck.Newest()));
    }

    private string Database(string name, string migration)
    {
        var file = Path.Combine(_folder.FullName, name);
        using var connection = new SqliteConnection($"Data Source={file};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            create table __EFMigrationsHistory (MigrationId text primary key, ProductVersion text not null);
            create table Panel (Id integer primary key);
            create table Configs (Id integer primary key);
            create table AspNetUsers (Id text primary key);
            """;
        command.ExecuteNonQuery();
        command.CommandText = "insert into __EFMigrationsHistory values ($id, '10.0.0')";
        command.Parameters.AddWithValue("$id", migration);
        command.ExecuteNonQuery();

        return file;
    }
}
