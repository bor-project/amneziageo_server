using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// A file of a package as the list beside the package names it.
/// </summary>
/// <param name="Sha256">The digest of the file.</param>
/// <param name="Mode">The permissions of the file.</param>
/// <param name="Size">The size of the file, in bytes.</param>
/// <param name="Offset">Where the packed file starts in the pack.</param>
/// <param name="Length">How many bytes of the pack the packed file takes.</param>
/// <param name="Path">Where the file lies in the package.</param>
public sealed record PackageEntry(string Sha256, UnixFileMode Mode, long Size, long Offset, long Length, string Path);

/// <summary>
/// Reads the list of the files of a package, each packed on its own one after another in the pack beside it.
/// </summary>
public static partial class PackageList
{
    /// <summary>
    /// The first line of a list this panel reads.
    /// </summary>
    public const string Head = "# amneziageo-server files 1";

    /// <summary>
    /// The largest list the panel reads, in bytes.
    /// </summary>
    public const int MaxSize = 4 * 1024 * 1024;

    /// <summary>
    /// The most files a list names.
    /// </summary>
    public const int MaxFiles = 20000;

    /// <summary>
    /// Reads a list and refuses one that does not hold together or does not cover a pack of the size given.
    /// </summary>
    public static IReadOnlyList<PackageEntry> Parse(byte[] text, long pack)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = Encoding.UTF8.GetString(text).Split('\n');
        if (lines[0] != Head)
        {
            throw new InvalidDataException("the list of files does not start with its head");
        }

        var entries = new List<PackageEntry>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var next = 0L;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            var entry = Entry(lines[i], i + 1);
            if (entry.Offset != next)
            {
                throw new InvalidDataException($"line {i + 1} of the list of files does not follow the one before in the pack");
            }

            if (!paths.Add(entry.Path) || entries.Count == MaxFiles)
            {
                throw new InvalidDataException($"line {i + 1} of the list of files names a file twice or one too many");
            }

            entries.Add(entry);
            next = entry.Offset + entry.Length;
        }

        if (entries.Count == 0 || next != pack)
        {
            throw new InvalidDataException("the list of files does not cover the pack");
        }

        return entries;
    }

    private static PackageEntry Entry(string line, int number)
    {
        var fields = line.Split(' ', 6);
        if (fields.Length != 6
            || !Digest().IsMatch(fields[0])
            || !Mode().IsMatch(fields[1])
            || !Count(fields[2], out var size)
            || !Count(fields[3], out var offset)
            || !Count(fields[4], out var length)
            || length == 0
            || size > PackageUpdater.MaxSize
            || length > PackageUpdater.MaxSize
            || !Safe(fields[5]))
        {
            throw new InvalidDataException($"line {number} of the list of files does not read");
        }

        var mode = (UnixFileMode)(Convert.ToInt32(fields[1], 8) & 0x1ff);

        return new PackageEntry(fields[0], mode, size, offset, length, fields[5]);
    }

    private static bool Count(string text, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool Safe(string path) =>
        path.Length > 0
        && !path.StartsWith('/')
        && !path.Any(char.IsControl)
        && !path.Contains('\\', StringComparison.Ordinal)
        && path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Digest();

    [GeneratedRegex("^[0-7]{3,4}$")]
    private static partial Regex Mode();
}
