namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// The settings a resolver starts with.
/// </summary>
public static class DnsDefaults
{
    /// <summary>
    /// The port the resolver takes questions on.
    /// </summary>
    public const int Port = 53;

    /// <summary>
    /// How long an answered address stays in the set of a rule, in minutes.
    /// </summary>
    public const int NameMinutes = 60;

    /// <summary>
    /// How many addresses of a rule stay in its set until newer ones push them out.
    /// </summary>
    public const int StandingAddresses = 128;

    /// <summary>
    /// How many answers are held back.
    /// </summary>
    public const int CacheSize = 4096;

    /// <summary>
    /// The shortest an answer is held back, in seconds.
    /// </summary>
    public const int MinTtl = 60;

    /// <summary>
    /// The longest an answer is held back, in seconds.
    /// </summary>
    public const int MaxTtl = 3600;

    /// <summary>
    /// How long a question waits for an upstream.
    /// </summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How long an answer waits for its new addresses to reach the sets of the rules.
    /// </summary>
    public static readonly TimeSpan Landing = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The name servers the questions are passed to, over HTTPS.
    /// </summary>
    public static readonly string[] Upstreams = ["https://1.1.1.1/dns-query", "https://8.8.4.4/dns-query"];

    /// <summary>
    /// The settings the panel starts with.
    /// </summary>
    public static DnsSettings Settings => new();
}
