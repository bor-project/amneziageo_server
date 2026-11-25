namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// Defines traffic conditions and name servers.
/// </summary>
public sealed record TrafficRule
{
    public long Id { get; init; }
    public string? DefaultKey { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Targets { get; init; } = [];
    public string DnsMode { get; init; } = TrafficDnsMode.Custom;
    public IReadOnlyList<string> Dns { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DnsMembers { get; init; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}
