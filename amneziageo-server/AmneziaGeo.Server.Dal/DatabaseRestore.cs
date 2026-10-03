using System.Globalization;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Leaves a backup the server was handed beside the database and puts it in place of the database as the server
/// starts over, keeping the database it replaces aside.
/// </summary>
public static class DatabaseRestore
{
    /// <summary>
    /// The name of the file beside the database a backup waits in until the server starts over.
    /// </summary>
    public const string Name = "restore.db";

    private static readonly string[] Journals = ["-wal", "-shm", "-journal"];

    /// <summary>
    /// Returns where a backup waits beside the database until the server starts over.
    /// </summary>
    public static string Staged(string database)
    {
        ArgumentNullException.ThrowIfNull(database);

        return Path.Combine(Folder(database), Name);
    }

    /// <summary>
    /// Leaves a backup beside the database for the next start, in place of one that waited there before.
    /// </summary>
    public static void Stage(string backup, string database)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(database);

        var staged = Staged(database);
        Clear(staged);
        File.Move(backup, staged, overwrite: true);
        Clear(backup);
    }

    /// <summary>
    /// Takes a backup that was turned down off the disk, along with what reading it left beside it.
    /// </summary>
    public static void Drop(string backup)
    {
        ArgumentNullException.ThrowIfNull(backup);

        File.Delete(backup);
        Clear(backup);
    }

    /// <summary>
    /// Puts the backup that waits beside the database in its place, open to its owner only, after moving the database
    /// and its journal into a folder of the backup directory named by the time; returns that folder, or null when no
    /// backup waits.
    /// </summary>
    public static string? Swap(string database, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(database);

        var staged = Staged(database);
        if (!File.Exists(staged))
        {
            return null;
        }

        var target = Path.GetFullPath(database);
        var aside = Path.Combine(
            Folder(target),
            "backup",
            now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-before-restore");
        Directory.CreateDirectory(aside);
        foreach (var file in Journals.Select(tail => target + tail).Prepend(target).Where(File.Exists))
        {
            File.Move(file, Path.Combine(aside, Path.GetFileName(file)), overwrite: true);
        }

        File.Move(staged, target);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return aside;
    }

    // Returns the directory the database lies in.
    private static string Folder(string database) => Path.GetDirectoryName(Path.GetFullPath(database)) ?? ".";

    // Takes the journal a reader of a database file left beside it off the disk.
    private static void Clear(string file)
    {
        foreach (var tail in Journals)
        {
            File.Delete(file + tail);
        }
    }
}
