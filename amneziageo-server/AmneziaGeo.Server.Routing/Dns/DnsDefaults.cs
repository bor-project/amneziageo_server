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
    /// The longest a client holds an answer with a stand-in address, in seconds.
    /// </summary>
    public const uint StandInSeconds = 300;

    /// <summary>
    /// How long a client that asked the resolver counts as one that asks it, in minutes.
    /// </summary>
    public const int AskedMinutes = 30;

    /// <summary>
    /// How long the tries of a client to reach a name server of its own are counted, in seconds.
    /// </summary>
    public const int TriedSeconds = 60;

    /// <summary>
    /// How fast a client that does not ask the resolver may try a name server of its own before it is let go.
    /// </summary>
    public const string TriedRate = "2/minute burst 3 packets";

    /// <summary>
    /// How long a client let go reaches the name servers of its own.
    /// </summary>
    public const string FreeTime = "1d";

    /// <summary>
    /// How long a private name server that gave no answer is passed over, in seconds.
    /// </summary>
    public const int QuietSeconds = 300;

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
    /// The IPv4 addresses of the name servers that answer over HTTPS.
    /// </summary>
    public static readonly string[] Https4 =
    [
        "1.0.0.1",
        "1.1.1.1",
        "8.8.4.4",
        "8.8.8.8",
        "9.9.9.9",
        "45.90.28.0/24",
        "45.90.30.0/24",
        "94.140.14.14",
        "94.140.15.15",
        "149.112.112.112",
        "208.67.220.220",
        "208.67.222.222",
    ];

    /// <summary>
    /// The IPv6 addresses of the name servers that answer over HTTPS.
    /// </summary>
    public static readonly string[] Https6 =
    [
        "2606:4700:4700::1001",
        "2606:4700:4700::1111",
        "2001:4860:4860::8844",
        "2001:4860:4860::8888",
        "2620:fe::fe",
        "2620:fe::9",
        "2a10:50c0::ad1:ff",
        "2a10:50c0::ad2:ff",
    ];

    /// <summary>
    /// The private ranges whose name servers get the questions asked of them.
    /// </summary>
    public static readonly string[] Private =
    [
        "10.0.0.0/8",
        "100.64.0.0/10",
        "169.254.0.0/16",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "fc00::/7",
        "fe80::/10",
    ];

    /// <summary>
    /// The settings the panel starts with.
    /// </summary>
    public static DnsSettings Settings => new();
}
