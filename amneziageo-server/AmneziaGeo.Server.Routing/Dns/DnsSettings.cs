namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// The name server the panel runs for the clients of the tunnels.
/// </summary>
public sealed record DnsSettings
{
    /// <summary>
    /// Whether the resolver answers the clients.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// The port the resolver takes questions on.
    /// </summary>
    public int Port { get; init; } = DnsDefaults.Port;

    /// <summary>
    /// The name servers the questions are passed to.
    /// </summary>
    public IReadOnlyList<string> Upstreams { get; init; } = DnsDefaults.Upstreams;

    /// <summary>
    /// The addresses the resolver listens on, empty for the addresses of the configurations.
    /// </summary>
    public IReadOnlyList<string> Listen { get; init; } = [];

    /// <summary>
    /// How long an answered address stays in the set of a rule, in minutes.
    /// </summary>
    public int NameMinutes { get; init; } = DnsDefaults.NameMinutes;

    /// <summary>
    /// How many answers are held back, zero for none.
    /// </summary>
    public int CacheSize { get; init; } = DnsDefaults.CacheSize;

    /// <summary>
    /// The shortest an answer is held back, in seconds.
    /// </summary>
    public int MinTtl { get; init; } = DnsDefaults.MinTtl;

    /// <summary>
    /// The longest an answer is held back, in seconds.
    /// </summary>
    public int MaxTtl { get; init; } = DnsDefaults.MaxTtl;

    /// <summary>
    /// Whether the questions the clients send elsewhere are taken by the resolver.
    /// </summary>
    public bool Intercept { get; init; } = true;

    /// <summary>
    /// Whether the clients are stopped from reaching name servers over TLS.
    /// </summary>
    public bool BlockDot { get; init; } = true;

    /// <summary>
    /// Whether the clients are stopped from reaching the known name servers over HTTPS.
    /// </summary>
    public bool BlockDoh { get; init; } = true;

    /// <summary>
    /// How long an answered address stays in the set of a rule.
    /// </summary>
    public TimeSpan NameLifetime => TimeSpan.FromMinutes(NameMinutes);

    /// <summary>
    /// Tells whether the other settings ask anything else of the resolver.
    /// </summary>
    public bool Differs(DnsSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return IsEnabled != other.IsEnabled
            || Port != other.Port
            || !Upstreams.SequenceEqual(other.Upstreams, StringComparer.Ordinal)
            || !Listen.SequenceEqual(other.Listen, StringComparer.Ordinal)
            || NameMinutes != other.NameMinutes
            || CacheSize != other.CacheSize
            || MinTtl != other.MinTtl
            || MaxTtl != other.MaxTtl
            || Intercept != other.Intercept
            || BlockDot != other.BlockDot
            || BlockDoh != other.BlockDoh;
    }
}
