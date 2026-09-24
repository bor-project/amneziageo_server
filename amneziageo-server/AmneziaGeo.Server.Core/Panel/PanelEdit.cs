namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// Turns the words of a command line into settings of the panel.
/// </summary>
public static class PanelEdit
{
    /// <summary>
    /// The address the panel listens on after a reset.
    /// </summary>
    public const string Loopback = "127.0.0.1";

    /// <summary>
    /// The word for every address of the host.
    /// </summary>
    public const string All = "all";

    /// <summary>
    /// The word for no names and no certificate.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// The word for a path no one guesses.
    /// </summary>
    public const string Random = "random";

    /// <summary>
    /// The channel of the releases alone.
    /// </summary>
    public const string Stable = "stable";

    /// <summary>
    /// The channel that takes the builds before a release as well.
    /// </summary>
    public const string Test = "test";

    /// <summary>
    /// Returns the settings a reset leaves: the loopback, the default port, a fresh path, any name, no certificate.
    /// </summary>
    public static PanelSettings Reset(PanelSettings held)
    {
        ArgumentNullException.ThrowIfNull(held);

        return held with
        {
            Listen = [Loopback],
            Domains = [],
            Port = PanelDefaults.Port,
            Path = PanelDefaults.FreshPath(),
            Certificate = string.Empty,
            CertificateKey = string.Empty,
        };
    }

    /// <summary>
    /// Returns the path a word names: a fresh one, the root, or the word itself.
    /// </summary>
    public static string PathOf(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        var trimmed = word.Trim();

        return trimmed switch
        {
            Random => PanelDefaults.FreshPath(),
            "/" => string.Empty,
            _ => trimmed.Trim('/'),
        };
    }

    /// <summary>
    /// Returns the items a word lists, none for the word that stands for nothing.
    /// </summary>
    public static IReadOnlyList<string> ListOf(string word, string nothing)
    {
        ArgumentNullException.ThrowIfNull(word);

        var trimmed = word.Trim();

        return string.Equals(trimmed, nothing, StringComparison.OrdinalIgnoreCase)
            ? []
            : PanelList.Split(trimmed.Replace(',', PanelList.Mark));
    }

    /// <summary>
    /// Returns the switch a word names, null when it names none.
    /// </summary>
    public static bool? SwitchOf(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return word.Trim().ToLowerInvariant() switch
        {
            "on" or "yes" or "true" => true,
            "off" or "no" or "false" => false,
            _ => null,
        };
    }

    /// <summary>
    /// Returns whether a channel takes prereleases, null when the word names no channel.
    /// </summary>
    public static bool? ChannelOf(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return word.Trim().ToLowerInvariant() switch
        {
            Stable => false,
            Test => true,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the channel the settings take releases from.
    /// </summary>
    public static string Channel(PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Prereleases ? Test : Stable;
    }
}
