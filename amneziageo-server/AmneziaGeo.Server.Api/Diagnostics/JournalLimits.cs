using AmneziaGeo.Server.Core.Diagnostics;

namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// The limits the file of the journal keeps to.
/// </summary>
/// <param name="MostRecords">How many records the file keeps at most.</param>
/// <param name="MostBytes">How many bytes the file and its write-ahead log take at most.</param>
public sealed record JournalLimits(long MostRecords, long MostBytes)
{
    /// <summary>
    /// The section of the settings the limits are read from.
    /// </summary>
    public const string Section = "Journal";

    /// <summary>
    /// The limits the panel keeps to when the settings give none.
    /// </summary>
    public static JournalLimits Default { get; } = new(JournalDefaults.MostRecords, JournalDefaults.MostBytes);

    /// <summary>
    /// Reads the limits from the settings, raising the ones below the lowest allowed.
    /// </summary>
    public static JournalLimits From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(Section);

        return new JournalLimits(
            Math.Max(section.GetValue(nameof(MostRecords), JournalDefaults.MostRecords), JournalDefaults.LeastRecords),
            Math.Max(section.GetValue(nameof(MostBytes), JournalDefaults.MostBytes), JournalDefaults.LeastBytes));
    }
}
