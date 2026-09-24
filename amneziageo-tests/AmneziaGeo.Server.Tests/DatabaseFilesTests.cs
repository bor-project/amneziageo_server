using AmneziaGeo.Server.Dal;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public class DatabaseFilesTests
{
    private const UnixFileMode Others = UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private const UnixFileMode Open = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode Shared = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;

    [Fact]
    public void ThePreparedDatabaseKeepsOtherUsersOut()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var bench = new Bench();

        Assert.Equal(default, File.GetUnixFileMode(bench.DatabasePath) & Others);
    }

    [Fact]
    public void TheCopiesLoseTheRightsOfOtherUsersAndKeepTheirGroup()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("amneziageo-files-").FullName;
        try
        {
            var database = Path.Combine(root, "server.db");
            var copy = Path.Combine(root, "backup", "20260924-120000-before-restore");
            Directory.CreateDirectory(copy);
            Write(database, Open);
            Write(database + "-wal", Open);
            Write(Path.Combine(copy, "server.db"), Open | UnixFileMode.OtherWrite);
            Write(Path.Combine(copy, "server.db-shm"), Shared);
            Write(Path.Combine(root, "health"), Open);

            DatabaseFiles.Hide(database, NullLogger.Instance);

            Assert.Equal(Open & ~Others, File.GetUnixFileMode(database));
            Assert.Equal(Open & ~Others, File.GetUnixFileMode(database + "-wal"));
            Assert.Equal(Open & ~Others, File.GetUnixFileMode(Path.Combine(copy, "server.db")));
            Assert.Equal(Shared, File.GetUnixFileMode(Path.Combine(copy, "server.db-shm")));
            Assert.Equal(Open, File.GetUnixFileMode(Path.Combine(root, "health")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void Write(string path, UnixFileMode mode)
    {
        File.WriteAllText(path, "x");
        File.SetUnixFileMode(path, mode);
    }
}
