using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// What the resolver is doing right now.
/// </summary>
public sealed record DnsStateBody(
    bool IsRunning,
    IReadOnlyList<string> Listening,
    string Fault,
    long Questions,
    long Cached,
    long Failed,
    long Added,
    int Waiting,
    int Names,
    DateTimeOffset? StartedUtc);

/// <summary>
/// What the reading of names out of the connections themselves has done since the panel started.
/// </summary>
/// <param name="Queue">Whether the first packets of the connections are read in the queue of the firewall.</param>
/// <param name="Port">Whether the first TCP connections are taken over.</param>
/// <param name="Packets">How many packets the queue showed.</param>
/// <param name="Named">How many connections gave their name in the queue.</param>
/// <param name="Reset">How many TCP connections a blocking rule cut.</param>
/// <param name="Missed">How many TCP connections stayed on a way the rules do not name, the rules taking the next ones.</param>
/// <param name="Taken">How many TCP connections were taken over.</param>
/// <param name="Open">How many TCP connections the panel carries right now.</param>
/// <param name="Learned">How many addresses the names read brought into the sets of the rules.</param>
public sealed record DnsReadingBody(
    bool Queue,
    bool Port,
    long Packets,
    long Named,
    long Reset,
    long Missed,
    long Taken,
    long Open,
    long Learned);

/// <summary>
/// The resolver as the panel reads it.
/// </summary>
public sealed record DnsResponse(
    bool IsEnabled,
    int Port,
    IReadOnlyList<string> Upstreams,
    string Outbound,
    IReadOnlyList<string> Listen,
    int NameMinutes,
    int CacheSize,
    int MinTtl,
    int MaxTtl,
    bool Intercept,
    bool BlockDot,
    bool BlockDoh,
    bool Pending,
    DnsStateBody State,
    DnsReadingBody Reading);

/// <summary>
/// The settings the resolver is changed with.
/// </summary>
public sealed record DnsRequest(
    bool IsEnabled,
    int Port,
    IReadOnlyList<string>? Upstreams,
    string? Outbound,
    IReadOnlyList<string>? Listen,
    int NameMinutes,
    int CacheSize,
    int MinTtl,
    int MaxTtl,
    bool Intercept,
    bool BlockDot,
    bool BlockDoh);

/// <summary>
/// Turns the resolver settings into what the panel reads and back.
/// </summary>
public static class DnsAnswers
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Returns the settings with what the resolver is doing; why the names of the connections are not read stands in
    /// for a fault when the resolver has none of its own.
    /// </summary>
    public static DnsResponse Resolver(
        DnsSettings settings,
        DnsState state,
        DnsSets sets,
        RoutePlan? plan,
        string? fault = null,
        DnsReadingBody? reading = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sets);

        return new DnsResponse(
            settings.IsEnabled,
            settings.Port,
            settings.Upstreams,
            settings.Outbound,
            settings.Listen,
            settings.NameMinutes,
            settings.CacheSize,
            settings.MinTtl,
            settings.MaxTtl,
            settings.Intercept,
            settings.BlockDot,
            settings.BlockDoh,
            state.Settings is { } running && settings.Differs(running),
            new DnsStateBody(
                state.IsRunning,
                state.Listening,
                state.Fault ?? state.Way ?? fault ?? string.Empty,
                state.Questions,
                state.Cached,
                state.Failed,
                sets.Added,
                sets.Waiting,
                DnsNames.Build(plan).Count,
                state.StartedUtc),
            reading ?? new DnsReadingBody(false, false, 0, 0, 0, 0, 0, 0, 0));
    }

    /// <summary>
    /// Returns what a request asks the resolver to become.
    /// </summary>
    public static DnsSettings Draft(DnsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new DnsSettings
        {
            IsEnabled = request.IsEnabled,
            Port = request.Port,
            Upstreams = Clean(request.Upstreams),
            Outbound = (request.Outbound ?? string.Empty).Trim(),
            Listen = Clean(request.Listen),
            NameMinutes = request.NameMinutes,
            CacheSize = request.CacheSize,
            MinTtl = request.MinTtl,
            MaxTtl = request.MaxTtl,
            Intercept = request.Intercept,
            BlockDot = request.BlockDot,
            BlockDoh = request.BlockDoh,
        };
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
    [
        .. (values ?? [])
            .SelectMany(value => value.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    ];
}
