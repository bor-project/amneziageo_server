namespace AmneziaGeo.Server.Geo.Files;

/// <summary>
/// Keeps the geo databases in a directory, one file per source.
/// </summary>
public sealed class DiskGeoFiles : IGeoFileStore
{
    /// <summary>
    /// Environment variable that moves the databases off their default path.
    /// </summary>
    public const string PathVariable = "AMNEZIAGEO_GEO";

    private const string DefaultDirectory = "/var/lib/amneziageo-server/geo";

    private readonly string _root;

    /// <summary>
    /// ctor
    /// </summary>
    public DiskGeoFiles(string? root = null)
    {
        _root = string.IsNullOrWhiteSpace(root) ? DefaultPath() : root;
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Returns the directory the server keeps the databases in.
    /// </summary>
    public static string DefaultPath() =>
        Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } set ? set : DefaultDirectory;

    /// <summary>
    /// Returns the directory the databases sit in next to a database file.
    /// </summary>
    public static string PathNear(string databasePath) =>
        Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } set
            ? set
            : Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "geo");

    /// <inheritdoc/>
    public Stream? OpenRead(string name)
    {
        var path = Path.Combine(_root, FileName(name));

        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    /// <inheritdoc/>
    public long Size(string name)
    {
        var file = new FileInfo(Path.Combine(_root, FileName(name)));

        return file.Exists ? file.Length : 0;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string name, byte[] data, CancellationToken ct = default)
    {
        var path = Path.Combine(_root, FileName(name));
        var pending = path + ".part";

        await File.WriteAllBytesAsync(pending, data, ct).ConfigureAwait(false);
        File.Move(pending, path, overwrite: true);
    }

    /// <inheritdoc/>
    public void Remove(string name)
    {
        var path = Path.Combine(_root, FileName(name));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    // A name outside the shape a source takes would reach outside the directory.
    private static string FileName(string name)
    {
        if (GeoSourceRules.CheckName(name) is { } fault)
        {
            throw new ArgumentException(fault.Message, nameof(name));
        }

        return name + ".dat";
    }
}
