namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// Where the name servers of a client file come from.
/// </summary>
public enum DnsChoice
{
    /// <summary>
    /// What the level above picks; at the endpoint, the resolver of the panel while the routing needs it and the servers
    /// of the endpoint otherwise.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// The resolver of the panel.
    /// </summary>
    Panel = 1,

    /// <summary>
    /// The name servers written next to the choice.
    /// </summary>
    Own = 2,
}

/// <summary>
/// Writes and reads where the name servers of a client file come from.
/// </summary>
public static class DnsChoiceName
{
    /// <summary>
    /// The name of leaving the choice to the level above.
    /// </summary>
    public const string Auto = "auto";

    /// <summary>
    /// The name of picking the resolver of the panel.
    /// </summary>
    public const string Panel = "panel";

    /// <summary>
    /// The name of picking name servers of its own.
    /// </summary>
    public const string Own = "own";

    /// <summary>
    /// Returns the name of a choice.
    /// </summary>
    public static string Of(DnsChoice choice) => choice switch
    {
        DnsChoice.Panel => Panel,
        DnsChoice.Own => Own,
        _ => Auto,
    };

    /// <summary>
    /// Returns what a name stands for, the given choice where the name is empty and -1 where it is unknown.
    /// </summary>
    public static DnsChoice Read(string? text, DnsChoice otherwise)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return otherwise;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            Auto => DnsChoice.Auto,
            Panel => DnsChoice.Panel,
            Own => DnsChoice.Own,
            _ => (DnsChoice)(-1),
        };
    }

    /// <summary>
    /// Tells whether a choice is one the panel knows.
    /// </summary>
    public static bool IsKnown(DnsChoice choice) => choice is DnsChoice.Auto or DnsChoice.Panel or DnsChoice.Own;
}
