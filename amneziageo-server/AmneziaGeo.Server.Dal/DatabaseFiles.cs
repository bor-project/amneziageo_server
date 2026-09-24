using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps the files of the database and of its copies away from other users of the host.
/// </summary>
public static class DatabaseFiles
{
    private const UnixFileMode Others = UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private static readonly EnumerationOptions Copies = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 1,
        IgnoreInaccessible = true,
    };

    /// <summary>
    /// Takes the rights of other users off the database, its journal and its copies in the backup directory.
    /// </summary>
    public static void Hide(string path, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        foreach (var file in Files(path).Where(File.Exists))
        {
            try
            {
                var mode = File.GetUnixFileMode(file);
                if ((mode & Others) != 0)
                {
                    File.SetUnixFileMode(file, mode & ~Others);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "the rights of {File} stayed as they were", file);
            }
        }
    }

    // Lists the files of the database and of the copies beside it.
    private static List<string> Files(string path)
    {
        var files = new List<string> { path, path + "-wal", path + "-shm" };
        var copies = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", "backup");
        if (Directory.Exists(copies))
        {
            files.AddRange(Directory.EnumerateFiles(copies, Path.GetFileName(path) + "*", Copies));
        }

        return files;
    }
}
