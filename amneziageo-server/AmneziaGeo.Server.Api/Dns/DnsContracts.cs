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
/// The resolver as the panel reads it.
/// </summary>
public sealed record DnsResponse(
    bool IsEnabled,
    int Port,
    IReadOnlyList<string> Upstreams,
    IReadOnlyList<string> Listen,
    int NameMinutes,
    int CacheSize,
    int MinTtl,
    int MaxTtl,
    bool Intercept,
    bool BlockDot,
    bool BlockDoh,
    bool Pending,
    DnsStateBody State);

/// <summary>
/// The settings the resolver is changed with.
/// </summary>
public sealed record DnsRequest(
    bool IsEnabled,
    int Port,
    IReadOnlyList<string>? Upstreams,
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
    /// Returns the settings with what the resolver is doing.
    /// </summary>
    public static DnsResponse Resolver(DnsSettings settings, DnsState state, DnsSets sets, RoutePlan? plan)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sets);

        return new DnsResponse(
            settings.IsEnabled,
            settings.Port,
            settings.Upstreams,
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
                state.Fault ?? string.Empty,
                state.Questions,
                state.Cached,
                state.Failed,
                sets.Added,
                sets.Waiting,
                DnsNames.Build(plan).Count,
                state.StartedUtc));
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
