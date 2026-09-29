namespace AmneziaGeo.Server.Core.Diagnostics;

/// <summary>
/// The limits of the journal of the panel and of its file.
/// </summary>
public static class JournalDefaults
{
    /// <summary>
    /// The file of the journal beside the database of the panel.
    /// </summary>
    public const string FileName = "journal.db";

    /// <summary>
    /// How many latest records the panel holds in memory and shows.
    /// </summary>
    public const int View = 500;

    /// <summary>
    /// How many records the file keeps at most.
    /// </summary>
    public const long MostRecords = 100_000;

    /// <summary>
    /// The lowest limit of records the settings may give.
    /// </summary>
    public const long LeastRecords = 1_000;

    /// <summary>
    /// How many bytes the file and its write-ahead log take at most.
    /// </summary>
    public const long MostBytes = 32L << 20;

    /// <summary>
    /// The lowest limit of bytes the settings may give.
    /// </summary>
    public const long LeastBytes = 1L << 20;

    /// <summary>
    /// The share of a limit, in percent, one trim frees.
    /// </summary>
    public const int Slack = 10;

    /// <summary>
    /// How many records wait for the file at most.
    /// </summary>
    public const int Queue = 10_000;

    /// <summary>
    /// How many records go to the file in one transaction.
    /// </summary>
    public const int Batch = 2_000;

    /// <summary>
    /// How often the waiting records go to the file.
    /// </summary>
    public static readonly TimeSpan Gather = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest category kept, in characters.
    /// </summary>
    public const int LongestCategory = 200;

    /// <summary>
    /// The longest message kept, in characters.
    /// </summary>
    public const int LongestMessage = 4_000;

    /// <summary>
    /// The longest fault kept, in characters.
    /// </summary>
    public const int LongestFault = 1_000;
}
