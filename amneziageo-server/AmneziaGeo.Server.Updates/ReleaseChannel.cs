namespace AmneziaGeo.Server.Updates;

/// <summary>
/// The channels a release of the client goes out on.
/// </summary>
public static class ReleaseChannel
{
    /// <summary>
    /// The channel every client reads.
    /// </summary>
    public const string Stable = "stable";

    /// <summary>
    /// The channel of the builds before a release.
    /// </summary>
    public const string Beta = "beta";

    /// <summary>
    /// The channel of the builds under test.
    /// </summary>
    public const string Test = "test";

    /// <summary>
    /// Every channel, the widest first.
    /// </summary>
    public static readonly string[] All = [Stable, Beta, Test];

    /// <summary>
    /// Tells whether a name is a channel.
    /// </summary>
    public static bool Known(string? channel) => channel is not null && Array.IndexOf(All, channel) >= 0;

    /// <summary>
    /// Tells whether a release of one channel reaches a client that reads another: a stable release reaches every
    /// client, a beta the readers of beta and test, a test build the readers of test alone.
    /// </summary>
    public static bool Reaches(string release, string reader) => Rank(release) <= Rank(reader);

    /// <summary>
    /// Returns where a channel stands, the widest first.
    /// </summary>
    public static int Rank(string channel) => channel switch
    {
        Stable => 0,
        Beta => 1,
        Test => 2,
        _ => 3,
    };
}
