namespace AmneziaGeo.Server.Geo;

/// <summary>
/// A geo database the server holds, together with what the last download left.
/// </summary>
public sealed record GeoSource
{
    /// <summary>
    /// The number the panel carries the source under.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The name the file is stored under.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Whether the source carries address ranges or domains.
    /// </summary>
    public string Kind { get; init; } = GeoKind.Ip;

    /// <summary>
    /// Where the file is downloaded from.
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// The place in the order sources override each other in.
    /// </summary>
    public int Position { get; init; }

    /// <summary>
    /// Whether rules are matched against the source.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// When the file was last downloaded.
    /// </summary>
    public DateTimeOffset? UpdatedUtc { get; init; }

    /// <summary>
    /// The digest of the stored file.
    /// </summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>
    /// How many countries or categories the stored file holds.
    /// </summary>
    public int EntryCount { get; init; }

    /// <summary>
    /// The size of the stored file, in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// The entity tag the server asks the next download against.
    /// </summary>
    public string ETag { get; init; } = string.Empty;

    /// <summary>
    /// The modification time the server asks the next download against.
    /// </summary>
    public string LastModified { get; init; } = string.Empty;

    /// <summary>
    /// What the last download ended with.
    /// </summary>
    public string LastError { get; init; } = string.Empty;
}
