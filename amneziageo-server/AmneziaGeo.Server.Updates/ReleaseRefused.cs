namespace AmneziaGeo.Server.Updates;

/// <summary>
/// Why the store of the releases would not do what it was asked.
/// </summary>
public sealed class ReleaseRefused : Exception
{
    /// <summary>
    /// ctor
    /// </summary>
    public ReleaseRefused(string code, string message, int status = 400)
        : base(message)
    {
        Code = code;
        Status = status;
    }

    /// <summary>
    /// The code of the refusal.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// The HTTP status the refusal answers with.
    /// </summary>
    public int Status { get; }

    /// <summary>
    /// Why the file of the refusal is not the file of the release, null for a refusal of something else.
    /// </summary>
    public FileFault? File { get; init; }

    /// <summary>
    /// Returns the refusal of a release that is not there.
    /// </summary>
    public static ReleaseRefused Missing(string tag) => new("release-missing", $"there is no release {tag}", 404);
}
