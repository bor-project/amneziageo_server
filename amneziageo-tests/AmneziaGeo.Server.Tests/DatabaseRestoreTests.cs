using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Tests;

public sealed class DatabaseRestoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 30, 45, TimeSpan.Zero);

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-restore-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void NothingMovesWhileNoBackupWaits()
    {
        var database = Write("panel.db", "live");

        Assert.Null(DatabaseRestore.Swap(database, Now));
        Assert.Equal("live", File.ReadAllText(database));
        Assert.False(Directory.Exists(Path.Combine(_folder.FullName, "backup")));
    }

    [Fact]
    public void AWaitingBackupTakesThePlaceOfTheDatabaseWhichIsSetAsideWithItsJournal()
    {
        var database = Write("panel.db", "live");
        Write("panel.db-wal", "wal");
        Write("panel.db-shm", "shm");
        var upload = Write("backup-1.db", "backup");
        DatabaseRestore.Stage(upload, database);

        var aside = DatabaseRestore.Swap(database, Now);

        Assert.NotNull(aside);
        Assert.Equal(Path.Combine(_folder.FullName, "backup", "20261003-123045-before-restore"), aside);
        Assert.Equal("backup", File.ReadAllText(database));
        Assert.Equal("live", File.ReadAllText(Path.Combine(aside, "panel.db")));
        Assert.Equal("wal", File.ReadAllText(Path.Combine(aside, "panel.db-wal")));
        Assert.Equal("shm", File.ReadAllText(Path.Combine(aside, "panel.db-shm")));
        Assert.Equal(["panel.db"], Names(_folder.FullName));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(database));
        }
    }

    [Fact]
    public void ALaterBackupReplacesOneThatWaitsAndLeavesNothingOfItselfBehind()
    {
        var database = Write("server.db", "live");
        DatabaseRestore.Stage(Write("backup-1.db", "first"), database);
        var second = Write("backup-2.db", "second");
        Write("backup-2.db-wal", string.Empty);
        Write("backup-2.db-shm", string.Empty);

        DatabaseRestore.Stage(second, database);

        Assert.Equal(Path.Combine(_folder.FullName, "restore.db"), DatabaseRestore.Staged(database));
        Assert.Equal("second", File.ReadAllText(DatabaseRestore.Staged(database)));
        Assert.Equal(["restore.db", "server.db"], Names(_folder.FullName));
    }

    [Fact]
    public void ABackupThatWasTurnedDownLeavesNothingBehind()
    {
        var upload = Write("backup-3.db", "junk");
        Write("backup-3.db-wal", string.Empty);
        Write("backup-3.db-shm", string.Empty);

        DatabaseRestore.Drop(upload);

        Assert.Empty(Names(_folder.FullName));
    }

    [Fact]
    public void AHostWithoutADatabaseTakesTheBackupAsItsFirst()
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        DatabaseRestore.Stage(Write("backup-4.db", "backup"), database);

        var aside = DatabaseRestore.Swap(database, Now);

        Assert.Equal("backup", File.ReadAllText(database));
        Assert.Empty(Names(aside!));
    }

    [Fact]
    public async Task ABackupTakenInComesUpInPlaceOfTheDatabaseAtTheNextStart()
    {
        var database = Path.Combine(_folder.FullName, "server.db");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database}").Options);
        await using (db)
        {
            await db.Database.MigrateAsync();
            var panel = new PanelStore(db);
            await panel.SaveAsync(PanelDefaults.Settings with { Port = 9443 }, CancellationToken.None);
            var backup = new DatabaseBackup(db);
            var copy = await backup.CopyAsync(CancellationToken.None);
            await panel.SaveAsync(PanelDefaults.Settings with { Port = 7443 }, CancellationToken.None);

            string received;
            await using (var stream = File.OpenRead(copy))
            {
                received = await backup.ReceiveAsync(stream, CancellationToken.None);
            }

            File.Delete(copy);
            Assert.Equal(_folder.FullName, Path.GetDirectoryName(received));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(received));
            }

            Assert.True(DatabaseCheck.Judge(received, DatabaseCheck.Newest()).IsSound);
            Assert.Equal(9443, PanelStore.Held(received)!.Port);

            backup.Stage(received);
        }

        Pools.ClearUnder(_folder.FullName);
        Assert.DoesNotContain(Names(_folder.FullName), name => name.StartsWith("backup-", StringComparison.Ordinal));

        var aside = DatabaseRestore.Swap(database, Now);

        Assert.Equal(9443, PanelStore.Held(database)!.Port);
        Assert.Equal(7443, PanelStore.Held(Path.Combine(aside!, "server.db"))!.Port);
        Assert.False(File.Exists(DatabaseRestore.Staged(database)));
    }

    private string Write(string name, string text)
    {
        var file = Path.Combine(_folder.FullName, name);
        File.WriteAllText(file, text);

        return file;
    }

    private static string[] Names(string folder) =>
        [.. Directory.GetFiles(folder).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)];
}
