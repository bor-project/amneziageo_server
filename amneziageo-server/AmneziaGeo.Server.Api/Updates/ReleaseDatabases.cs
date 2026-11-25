using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// Binds database snapshots to release identities.
/// </summary>
public static class ReleaseDatabases
{
    private static readonly string[] Suffixes = [string.Empty, "-wal", "-shm", "-journal"];

    public static string Save(string data, string identity)
    {
        if (!File.Exists(Path.Combine(data, "server.db")))
        {
            return string.Empty;
        }
        var root = Path.GetFullPath(data);
        var folder = Path.Combine(root, "backup", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-database." + Guid.NewGuid().ToString("N"));
        PrivateDirectory(Path.Combine(root, "backup"));
        PrivateDirectory(folder);
        foreach (var suffix in Suffixes)
        {
            var source = Path.Combine(root, "server.db" + suffix);
            if (File.Exists(source))
            {
                var target = Path.Combine(folder, Path.GetFileName(source));
                Copy(source, target);
                PrivateFile(target);
            }
        }
        File.WriteAllText(Path.Combine(folder, "build"), identity + "\n");
        var bindings = Path.Combine(root, "rollback");
        PrivateDirectory(bindings);
        var file = Path.Combine(bindings, Key(identity));
        var stage = Path.Combine(bindings, ".binding." + Guid.NewGuid().ToString("N"));
        File.WriteAllText(stage, identity + "\nbackup/" + Path.GetFileName(folder) + "\n");
        PrivateFile(Path.Combine(folder, "build"));
        PrivateFile(stage);
        File.Move(stage, file, overwrite: true);
        return folder;
    }

    public static string? Find(string data, string identity)
    {
        var root = Path.GetFullPath(data);
        var binding = Path.Combine(root, "rollback", Key(identity));
        if (!File.Exists(binding))
        {
            return null;
        }
        var lines = File.ReadAllLines(binding);
        if (lines.Length != 2 || lines[0] != identity || Path.IsPathRooted(lines[1]))
        {
            return null;
        }
        var backup = Path.GetFullPath(Path.Combine(root, lines[1]));
        if (!backup.StartsWith(Path.Combine(root, "backup") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(Path.Combine(backup, "server.db"))
            || !File.Exists(Path.Combine(backup, "build"))
            || File.ReadAllText(Path.Combine(backup, "build")).TrimEnd('\r', '\n') != identity)
        {
            return null;
        }
        return backup;
    }

    public static void Restore(string data, string backup)
    {
        if (backup.Length == 0)
        {
            return;
        }
        if (!File.Exists(Path.Combine(backup, "server.db")))
        {
            throw new FileNotFoundException("the matching database snapshot is missing", backup);
        }
        var stage = Path.Combine(data, ".database-restore." + Guid.NewGuid().ToString("N"));
        var aside = Path.Combine(data, "backup", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-before-restore." + Guid.NewGuid().ToString("N"));
        PrivateDirectory(stage);
        foreach (var suffix in Suffixes)
        {
            var source = Path.Combine(backup, "server.db" + suffix);
            if (File.Exists(source))
            {
                var target = Path.Combine(stage, Path.GetFileName(source));
                Copy(source, target);
                PrivateFile(target);
            }
        }
        PrivateDirectory(Path.Combine(data, "backup"));
        PrivateDirectory(aside);
        var moved = false;
        try
        {
            foreach (var suffix in Suffixes)
            {
                var source = Path.Combine(data, "server.db" + suffix);
                if (File.Exists(source))
                {
                    File.Move(source, Path.Combine(aside, Path.GetFileName(source)));
                }
            }
            moved = true;
            foreach (var suffix in Suffixes)
            {
                var source = Path.Combine(stage, "server.db" + suffix);
                if (File.Exists(source))
                {
                    File.Move(source, Path.Combine(data, Path.GetFileName(source)));
                }
            }
        }
        catch
        {
            if (moved)
            {
                foreach (var suffix in Suffixes)
                {
                    File.Delete(Path.Combine(data, "server.db" + suffix));
                }
            }
            foreach (var suffix in Suffixes)
            {
                var source = Path.Combine(aside, "server.db" + suffix);
                if (File.Exists(source))
                {
                    File.Move(source, Path.Combine(data, Path.GetFileName(source)), overwrite: true);
                }
            }
            throw;
        }
        finally
        {
            Directory.Delete(stage, recursive: true);
        }
    }

    public static void Prune(string data, params string[] identities)
    {
        var root = Path.GetFullPath(data);
        var bindings = Path.Combine(root, "rollback");
        var keep = new HashSet<string>(StringComparer.Ordinal);
        if (Directory.Exists(bindings))
        {
            foreach (var binding in Directory.EnumerateFiles(bindings).Where(file => Path.GetFileName(file).Length == 64))
            {
                var lines = File.ReadAllLines(binding);
                if (lines.Length != 2)
                {
                    continue;
                }
                if (lines[0].StartsWith("docker:", StringComparison.Ordinal) && !identities.Contains(lines[0], StringComparer.Ordinal))
                {
                    File.Delete(binding);
                }
                else if (Find(root, lines[0]) is { } snapshot)
                {
                    keep.Add(snapshot);
                }
            }
        }
        var backups = Path.Combine(root, "backup");
        if (!Directory.Exists(backups))
        {
            return;
        }
        foreach (var old in Directory.EnumerateDirectories(backups).Where(folder => !keep.Contains(folder)).OrderDescending(StringComparer.Ordinal).Skip(5))
        {
            Directory.Delete(old, recursive: true);
        }
    }

    private static void Copy(string source, string target)
    {
        if (!OperatingSystem.IsLinux())
        {
            File.Copy(source, target);
            return;
        }
        var start = new ProcessStartInfo("cp") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in new[] { "-p", "--", source, target })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new IOException("the database copy did not start");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException("the database copy failed: " + error);
        }
    }

    private static void PrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(path, mode);
        File.SetUnixFileMode(path, mode);
    }

    private static void PrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string Key(string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (identity.Contains('\n', StringComparison.Ordinal) || identity.Contains('\r', StringComparison.Ordinal))
        {
            throw new ArgumentException("the release identity contains a line break", nameof(identity));
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
