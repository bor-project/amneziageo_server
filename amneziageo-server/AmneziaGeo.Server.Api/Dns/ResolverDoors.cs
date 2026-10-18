using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Firewall;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// Tells which interfaces the firewall of the host keeps away from the resolver of the panel.
/// </summary>
public sealed class ResolverDoors
{
    private static readonly TimeSpan Kept = TimeSpan.FromSeconds(15);

    private readonly FirewallHost _host;

    private readonly TimeProvider _time;

    private Seen? _seen;

    /// <summary>
    /// ctor
    /// </summary>
    public ResolverDoors(FirewallHost host, TimeProvider time)
    {
        _host = host;
        _time = time;
    }

    /// <summary>
    /// Returns the interfaces whose clients cannot reach the resolver through the firewall of the host.
    /// </summary>
    public async Task<IReadOnlyList<string>> KeptOutAsync(
        DnsSettings resolver,
        IReadOnlyList<ServerConfig> configs,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(configs);

        if (!resolver.IsEnabled)
        {
            return [];
        }

        var names = configs
            .Where(config => config.IsEnabled && FirewallPlan.Answers(resolver, config))
            .Select(config => config.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var key = $"{resolver.Port}:{string.Join(",", names)}";
        var now = _time.GetUtcNow();
        if (Volatile.Read(ref _seen) is { } seen && seen.Key == key && now - seen.At < Kept)
        {
            return seen.Out;
        }

        var shut = await _host.KeptOutAsync(names, resolver.Port, ct).ConfigureAwait(false);
        Volatile.Write(ref _seen, new Seen(key, now, shut));

        return shut;
    }

    private sealed record Seen(string Key, DateTimeOffset At, IReadOnlyList<string> Out);
}
