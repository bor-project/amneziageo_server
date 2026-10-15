using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// Lays again the interfaces of the endpoints that are on when the host lost them or brought them down.
/// </summary>
public sealed class ConfigWatch
{
    /// <summary>
    /// How long an interface the host refused waits before it is tried again, unless the endpoints change first.
    /// </summary>
    public static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);

    private readonly EndpointKeeper _keeper;

    private readonly EndpointHost _host;

    private readonly DnsHost _resolver;

    private readonly DatabaseVersion _version;

    private readonly TimeProvider _time;

    private readonly ILogger<ConfigWatch> _logger;

    private readonly Dictionary<string, Refusal> _refused = new(StringComparer.Ordinal);

    private IReadOnlyList<ServerConfig> _configs = [];

    private long? _seen;

    /// <summary>
    /// ctor
    /// </summary>
    public ConfigWatch(
        EndpointKeeper keeper,
        EndpointHost host,
        DnsHost resolver,
        DatabaseVersion version,
        TimeProvider time,
        ILogger<ConfigWatch> logger)
    {
        _keeper = keeper;
        _host = host;
        _resolver = resolver;
        _version = version;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Looks at the interfaces of the endpoints when the host or the endpoints changed or a refused interface is due to
    /// be tried again, and lays again the ones the host lost.
    /// </summary>
    public async Task LookAsync(IServiceProvider services, bool changed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        try
        {
            var (configs, fresh) = await ConfigsAsync(services, ct).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            if (!changed && !fresh && !_refused.Values.Any(one => one.Again <= now))
            {
                return;
            }

            var lost = _keeper.Lost(configs);
            if (lost.Count == 0)
            {
                _refused.Clear();

                return;
            }

            if (Due(lost, fresh, now).Count == 0 || !await MendAsync(services, now, ct).ConfigureAwait(false))
            {
                return;
            }

            await services.GetRequiredService<RouteApplier>().SettleAsync(ct).ConfigureAwait(false);
            await _resolver.RebindAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "the interfaces of the endpoints were not looked at");
        }
    }

    // Mends what is still lost and due once the endpoints are held, telling whether an interface came back.
    private async Task<bool> MendAsync(IServiceProvider services, DateTimeOffset now, CancellationToken ct)
    {
        using var hold = await _keeper.HoldAsync(ct).ConfigureAwait(false);
        var (configs, fresh) = await ConfigsAsync(services, ct).ConfigureAwait(false);
        var lost = Due(_keeper.Lost(configs), fresh, now);
        if (lost.Count == 0)
        {
            return false;
        }

        var clients = await services.GetRequiredService<ClientStore>().ListAsync(ct).ConfigureAwait(false);
        var back = false;
        foreach (var mended in await _keeper.MendAsync(lost, clients, ct).ConfigureAwait(false))
        {
            back |= Say(mended, now);
        }

        if (back)
        {
            await _host.FirewallAsync(configs, clients, ct).ConfigureAwait(false);
        }

        return back;
    }

    // Returns the endpoints, read again only when the database changed, and whether they were.
    private async Task<(IReadOnlyList<ServerConfig> Configs, bool Fresh)> ConfigsAsync(
        IServiceProvider services,
        CancellationToken ct)
    {
        var version = _version.Read();
        if (version is not null && version == _seen)
        {
            return (_configs, false);
        }

        _configs = await services.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false);
        _seen = version;

        return (_configs, true);
    }

    // Returns the lost endpoints that may be tried now: all of them when the endpoints changed, otherwise the ones the
    // host did not refuse within the wait.
    private List<ServerConfig> Due(IReadOnlyList<ServerConfig> lost, bool fresh, DateTimeOffset now) =>
        [.. lost.Where(config => fresh || !_refused.TryGetValue(config.Name, out var refusal) || refusal.Again <= now)];

    // Writes what became of an interface to the log, a refusal once until its reason changes.
    private bool Say(EndpointMended mended, DateTimeOffset now)
    {
        var name = mended.Sync.Name;
        if (!mended.Sync.IsDone)
        {
            if (!_refused.TryGetValue(name, out var said) || !string.Equals(said.Message, mended.Sync.Message, StringComparison.Ordinal))
            {
                _logger.LogWarning("the interface of the endpoint '{Endpoint}' did not come back: {Reason}", name, mended.Sync.Message);
            }

            _refused[name] = new Refusal(mended.Sync.Message, now + Retry);

            return false;
        }

        _refused.Remove(name);
        if (mended.WasGone)
        {
            _logger.LogWarning(
                "the interface of the endpoint '{Endpoint}' was gone and is laid again with {Count} clients",
                name,
                mended.Clients);
        }
        else
        {
            _logger.LogWarning("the interface of the endpoint '{Endpoint}' was down and is up again", name);
        }

        return true;
    }

    private sealed record Refusal(string Message, DateTimeOffset Again);
}
