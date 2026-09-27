namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// The fixed values of the connection log.
/// </summary>
public static class AccessDefaults
{
    /// <summary>
    /// The log group the firewall hands the new connections of the clients to.
    /// </summary>
    public const ushort Group = 7317;

    /// <summary>
    /// The bytes of a packet the kernel copies into a record.
    /// </summary>
    public const int Snap = 96;

    /// <summary>
    /// The bytes the socket holds for records the panel has not read yet.
    /// </summary>
    public const int Buffer = 4 << 20;

    /// <summary>
    /// How long one read of the socket waits before it looks whether the log goes on.
    /// </summary>
    public static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The most records that wait for the database.
    /// </summary>
    public const int Queue = 65536;

    /// <summary>
    /// The most records written to the database in one step.
    /// </summary>
    public const int Batch = 2000;

    /// <summary>
    /// How long the writer gathers records before it writes them.
    /// </summary>
    public static readonly TimeSpan Gather = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a connection that went out is watched for what comes back before its record is written.
    /// </summary>
    public static readonly TimeSpan Answer = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The longest a connection that keeps asking again is watched from its first packet.
    /// </summary>
    public static readonly TimeSpan LongestAnswer = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a connection that asks again the same way keeps the one record it has.
    /// </summary>
    public static readonly TimeSpan Repeat = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The most connections watched for what comes back at once.
    /// </summary>
    public const int MostWatched = 65536;

    /// <summary>
    /// The bit of the connection mark that has the firewall hand what comes back to a connection to the log.
    /// </summary>
    public const uint Watching = 0x0001_0000;

    /// <summary>
    /// The bit of the connection mark that tells the first packet back went to the log.
    /// </summary>
    public const uint Answered = 0x0002_0000;

    /// <summary>
    /// The bit of the connection mark that tells the packet back that settles the outcome went to the log.
    /// </summary>
    public const uint Settled = 0x0004_0000;

    /// <summary>
    /// How often the records past their time are removed.
    /// </summary>
    public static readonly TimeSpan Trimming = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The days a record is kept when nothing else is set.
    /// </summary>
    public const int Days = 7;

    /// <summary>
    /// The fewest days a record can be kept.
    /// </summary>
    public const int LeastDays = 1;

    /// <summary>
    /// The most days a record can be kept.
    /// </summary>
    public const int MostDays = 90;

    /// <summary>
    /// The most records the log keeps; the oldest go first.
    /// </summary>
    public const int MostRecords = 1_000_000;

    /// <summary>
    /// The names one generation of the name book holds before it is turned over.
    /// </summary>
    public const int Names = 100_000;

    /// <summary>
    /// The records one page of the log carries when nothing else is asked.
    /// </summary>
    public const int Page = 200;

    /// <summary>
    /// The most records one page of the log carries.
    /// </summary>
    public const int MostPage = 1000;

    /// <summary>
    /// The most groups a summary carries.
    /// </summary>
    public const int MostGroups = 500;

    /// <summary>
    /// The file the log is kept in, beside the database of the panel.
    /// </summary>
    public const string FileName = "access.db";
}
