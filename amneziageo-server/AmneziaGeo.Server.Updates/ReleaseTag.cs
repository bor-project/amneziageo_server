using System.Globalization;
using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Updates;

/// <summary>
/// The name of the folder of a release: its version, its channel and the number of the build within the channel.
/// </summary>
/// <param name="Version">The version of the client in the release, in four numbers.</param>
/// <param name="Channel">The channel the release goes out on.</param>
/// <param name="Number">The number of the build within the channel, 0 for a stable release.</param>
public sealed partial record ReleaseTag(Version Version, string Channel, int Number) : IComparable<ReleaseTag>
{
    /// <summary>
    /// Reads a tag like v1.10.9.0, v1.10.15.0-beta.1 or v1.10.16.0-test.1.
    /// </summary>
    public static bool TryParse(string? text, out ReleaseTag tag)
    {
        tag = null!;
        if (text is null || Pattern().Match(text) is not { Success: true } match)
        {
            return false;
        }

        if (!TryVersion(match.Groups["version"].Value, out var version))
        {
            return false;
        }

        if (!match.Groups["channel"].Success)
        {
            tag = new ReleaseTag(version, ReleaseChannel.Stable, 0);

            return true;
        }

        var channel = match.Groups["channel"].Value;
        var number = int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
        if (!ReleaseChannel.Known(channel) || channel == ReleaseChannel.Stable || number <= 0)
        {
            return false;
        }

        tag = new ReleaseTag(version, channel, number);

        return true;
    }

    /// <summary>
    /// Reads a version of four numbers.
    /// </summary>
    public static bool TryVersion(string? text, out Version version)
    {
        version = null!;
        if (text is null || !Version.TryParse(text.Trim(), out var parsed) || parsed.Revision < 0)
        {
            return false;
        }

        version = parsed;

        return true;
    }

    /// <summary>
    /// Returns the tag as the folder of the release is named.
    /// </summary>
    public override string ToString() =>
        Channel == ReleaseChannel.Stable
            ? "v" + Version
            : string.Create(CultureInfo.InvariantCulture, $"v{Version}-{Channel}.{Number}");

    /// <summary>
    /// Orders tags from the oldest to the newest: by version, then a stable release over a beta over a test build
    /// of the same version, then by the number of the build.
    /// </summary>
    public int CompareTo(ReleaseTag? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byVersion = Version.CompareTo(other.Version);
        if (byVersion != 0)
        {
            return byVersion;
        }

        var byChannel = ReleaseChannel.Rank(other.Channel).CompareTo(ReleaseChannel.Rank(Channel));

        return byChannel != 0 ? byChannel : Number.CompareTo(other.Number);
    }

    [GeneratedRegex(@"^v(?<version>\d+\.\d+\.\d+\.\d+)(?:-(?<channel>[a-z]+)\.(?<number>\d{1,4}))?$")]
    private static partial Regex Pattern();
}
