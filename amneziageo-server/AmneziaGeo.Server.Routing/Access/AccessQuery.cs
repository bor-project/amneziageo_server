namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// Which records of the log are asked for.
/// </summary>
public sealed record AccessQuery
{
    /// <summary>
    /// The earliest time a record is taken from.
    /// </summary>
    public DateTimeOffset From { get; init; }

    /// <summary>
    /// The time a record is taken before.
    /// </summary>
    public DateTimeOffset To { get; init; }

    /// <summary>
    /// The client or the source address the records came from, or empty for every one.
    /// </summary>
    public string Client { get; init; } = string.Empty;

    /// <summary>
    /// How the records were carried, or empty for every way.
    /// </summary>
    public string Verdict { get; init; } = string.Empty;

    /// <summary>
    /// The outbound the records left through, or empty for every one.
    /// </summary>
    public string Way { get; init; } = string.Empty;

    /// <summary>
    /// Whether the records left through the uplink of the host or were handed to another server, or empty for both.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// What came back to the records, failed for every outcome of a connection that did not get through, or empty.
    /// </summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>
    /// A part of the name, the address or the rule the records carry, or empty.
    /// </summary>
    public string Search { get; init; } = string.Empty;

    /// <summary>
    /// The record the page ends before, or null for the newest.
    /// </summary>
    public long? Before { get; init; }

    /// <summary>
    /// The most records the page carries.
    /// </summary>
    public int Limit { get; init; } = AccessDefaults.Page;
}

/// <summary>
/// How the summary puts the records together.
/// </summary>
public static class AccessGrouping
{
    /// <summary>
    /// By the domain the name belongs to.
    /// </summary>
    public const string Domain = "domain";

    /// <summary>
    /// By the whole name, or the address when there is none.
    /// </summary>
    public const string Name = "name";

    /// <summary>
    /// By the rule that decided.
    /// </summary>
    public const string Rule = "rule";

    /// <summary>
    /// By the outbound the records left through.
    /// </summary>
    public const string Way = "way";

    /// <summary>
    /// By the client the records came from.
    /// </summary>
    public const string Client = "client";

    /// <summary>
    /// Every way the summary can put the records together.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Domain, Name, Rule, Way, Client];
}

/// <summary>
/// How many records share one value.
/// </summary>
/// <param name="Key">The value.</param>
/// <param name="Count">The records that carry it.</param>
public sealed record AccessCount(string Key, long Count);

/// <summary>
/// The records that share a key, a verdict, a way and a rule.
/// </summary>
/// <param name="Key">What the records are put together by.</param>
/// <param name="Verdict">How the records were carried.</param>
/// <param name="Way">The outbound they left through, or the guard that dropped them.</param>
/// <param name="Via">The balancer that picked the outbound, or empty.</param>
/// <param name="RuleName">The rule that decided, or empty.</param>
/// <param name="Clients">How many client addresses the records came from.</param>
/// <param name="Count">How many records there are.</param>
/// <param name="Ok">How many of them got through.</param>
/// <param name="Failed">How many of them did not get through.</param>
/// <param name="First">The earliest of them.</param>
/// <param name="Last">The latest of them.</param>
public sealed record AccessGroup(
    string Key,
    string Verdict,
    string Way,
    string Via,
    string RuleName,
    int Clients,
    long Count,
    long Ok,
    long Failed,
    DateTimeOffset First,
    DateTimeOffset Last);

/// <summary>
/// What the records of a time span came to.
/// </summary>
/// <param name="Total">How many records there are.</param>
/// <param name="Verdicts">How many were carried each way.</param>
/// <param name="Ways">How many left through each outbound.</param>
/// <param name="Paths">How many left through the uplink of the host and how many were handed to another server.</param>
/// <param name="Outcomes">How many came to each outcome.</param>
/// <param name="Groups">The records put together, the largest first.</param>
/// <param name="IsCut">Whether groups past the limit were left out.</param>
public sealed record AccessSummary(
    long Total,
    IReadOnlyList<AccessCount> Verdicts,
    IReadOnlyList<AccessCount> Ways,
    IReadOnlyList<AccessCount> Paths,
    IReadOnlyList<AccessCount> Outcomes,
    IReadOnlyList<AccessGroup> Groups,
    bool IsCut);

/// <summary>
/// What the log holds.
/// </summary>
/// <param name="Records">How many records there are.</param>
/// <param name="Oldest">The earliest record, or null when there is none.</param>
/// <param name="Newest">The latest record, or null when there is none.</param>
/// <param name="Bytes">The size of the files of the log.</param>
public sealed record AccessStock(long Records, DateTimeOffset? Oldest, DateTimeOffset? Newest, long Bytes);
