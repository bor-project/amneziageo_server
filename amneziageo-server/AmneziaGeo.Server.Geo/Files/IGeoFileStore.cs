namespace AmneziaGeo.Server.Geo.Files;

/// <summary>
/// Where the downloaded geo databases are kept.
/// </summary>
public interface IGeoFileStore
{
    /// <summary>
    /// Opens the file of a source, or null when it is not there.
    /// </summary>
    Stream? OpenRead(string name);

    /// <summary>
    /// Returns the size of the file of a source, or zero when it is not there.
    /// </summary>
    long Size(string name);

    /// <summary>
    /// Writes the file of a source.
    /// </summary>
    Task WriteAsync(string name, byte[] data, CancellationToken ct = default);

    /// <summary>
    /// Removes the file of a source.
    /// </summary>
    void Remove(string name);
}
