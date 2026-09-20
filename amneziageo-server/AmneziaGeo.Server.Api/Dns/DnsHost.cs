using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// Runs the resolver and carries the answered addresses to the sets of the rules.
/// </summary>
public sealed class DnsHost : BackgroundService
{
    private static readonly TimeSpan Between = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan Saving = TimeSpan.FromMinutes(1);

    private const int WarmPerTick = 16;

    private readonly SemaphoreSlim _turn = new(1, 1);

    private readonly IServiceScopeFactory _scopes;

    private readonly RoutePlans _plans;

    private readonly DnsSets _sets;

    private readonly DnsState _state;

    private readonly TimeProvider _time;

    private readonly ILogger<DnsHost> _logger;

    private DnsServer? _server;

    private IDnsUpstream? _upstream;

    private readonly Dictionary<DnsWarmName, DnsWarmTry> _asked = new();

    private DnsSettings _settings = DnsDefaults.Settings;

    private long _turns;

    private long _saved;

    private DateTimeOffset _saving;

    private bool _restored;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsHost(
        IServiceScopeFactory scopes,
        RoutePlans plans,
        DnsSets sets,
        DnsState state,
        TimeProvider time,
        ILogger<DnsHost> logger)
    {
        _scopes = scopes;
        _plans = plans;
        _sets = sets;
        _state = state;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// The settings the resolver runs with.
    /// </summary>
    public DnsSettings Settings => _settings;

    /// <summary>
    /// The outbound the resolver asks through right now, empty when it is off or leaves through the host.
    /// </summary>
    public string Asking => _settings.IsEnabled ? _settings.Outbound : string.Empty;

    /// <summary>
    /// Returns the addresses a name answers with through the way out of the resolver, or null when it is not running.
    /// </summary>
    public async Task<IReadOnlyList<IPAddress>?> AskAsync(string name, CancellationToken ct) =>
        _upstream is { } upstream ? await DnsLookup.AskAsync(upstream, name, ct).ConfigureAwait(false) : null;

    /// <summary>
    /// Takes the settings anew and starts the resolver over.
    /// </summary>
    public Task RestartAsync(CancellationToken ct) => StartAsync(true, ct);

    /// <summary>
    /// Starts the resolver over on the addresses of the configurations with the settings it runs with.
    /// </summary>
    public Task RebindAsync(CancellationToken ct) => StartAsync(false, ct);

    /// <summary>
    /// Carries a new name of the outbound or the balancer the resolver asks through into the settings it runs with.
    /// </summary>
    public async Task FollowAsync(string old, string anew, CancellationToken ct)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!string.Equals(_settings.Outbound, old, StringComparison.Ordinal))
            {
                return;
            }

            Volatile.Write(ref _settings, _settings with { Outbound = anew });
            _state.Took(_settings);
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken ct)
    {
        _server?.Stop();
        _server = null;
        _upstream = null;
        _state.Stopped();
        await base.StopAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try
        {
            await RestartAsync(stopping).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Between, _time);
            while (await timer.WaitForNextTickAsync(stopping).ConfigureAwait(false))
            {
                Look();
                await WarmAsync(stopping).ConfigureAwait(false);
                await FlushAsync(stopping).ConfigureAwait(false);
                await SaveAsync(stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task StartAsync(bool anew, CancellationToken ct)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _server?.Stop();
            _server = null;
            using var scope = _scopes.CreateScope();
            _settings = anew || _state.Settings is null
                ? await scope.ServiceProvider.GetRequiredService<DnsStore>().ReadAsync(ct).ConfigureAwait(false)
                : _state.Settings;
            _state.Took(_settings);
            if (!_settings.IsEnabled)
            {
                _state.Stopped();
                return;
            }

            await RestoreAsync(scope.ServiceProvider, ct).ConfigureAwait(false);
            if (_plans.Held is null)
            {
                await scope.ServiceProvider.GetRequiredService<RouteApplier>().BuildAsync(ct).ConfigureAwait(false);
            }

            var addresses = await AddressesAsync(scope.ServiceProvider, _settings, ct).ConfigureAwait(false);
            if (addresses.Count == 0)
            {
                _state.Stopped("there is no address to answer the clients on");
                return;
            }

            Serve(addresses);
        }
        finally
        {
            _turn.Release();
        }
    }

    private void Serve(IReadOnlyList<IPAddress> addresses)
    {
        var upstream = new DnsUpstream(
            _settings.Upstreams,
            DnsDefaults.Wait,
            () => Way(Volatile.Read(ref _settings), Interlocked.Increment(ref _turns)).Mark);
        var resolver = new DnsResolver(
            upstream,
            new DnsCache(_settings.CacheSize, _time),
            _sets,
            () => _plans.Held,
            _settings,
            _state,
            FlushAsync);

        var server = new DnsServer(resolver);
        var taken = default(IReadOnlyList<IPAddress>);
        try
        {
            taken = server.Start(addresses, _settings.Port);
        }
        catch (SocketException ex)
        {
            _state.Stopped(ex.Message);
            _logger.LogWarning(ex, "the resolver did not take the addresses of the tunnels");
            return;
        }

        _server = server;
        _upstream = upstream;
        _asked.Clear();
        _state.Started([.. taken.Select(address => address.ToString())], _time.GetUtcNow());
        _logger.LogInformation(
            "the resolver answers on {Addresses} port {Port} and asks through {Exit}",
            string.Join(", ", _state.Listening),
            _settings.Port,
            _settings.Outbound.Length == 0 ? "the host" : _settings.Outbound);
        Look();
    }

    private DnsWay Way(DnsSettings settings, long turn) =>
        DnsExit.Way(settings, _plans.Held?.Ways ?? RouteWays.None, turn);

    private void Look()
    {
        if (_upstream is null)
        {
            return;
        }

        var fault = Way(_settings, 0).Fault?.Message;
        if (fault == _state.Way)
        {
            return;
        }

        _state.Leaves(fault);
        if (fault is null)
        {
            _logger.LogInformation("the resolver asks through '{Outbound}' again", _settings.Outbound);
        }
        else
        {
            _logger.LogWarning("the way out of the resolver is broken: {Fault}", fault);
        }
    }

    private async Task WarmAsync(CancellationToken ct)
    {
        if (_upstream is not { } upstream)
        {
            return;
        }

        var lifetime = _settings.NameLifetime;
        var names = DnsWarm.Names(_plans.Held);
        Forget(names);

        var asking = _time.GetUtcNow();
        var wanted = names
            .Where(one => !_asked.TryGetValue(one, out var last) || last.Again <= asking)
            .Take(WarmPerTick)
            .ToArray();

        if (wanted.Length == 0)
        {
            return;
        }

        var found = await DnsWarm.AskAsync(upstream, wanted, ct).ConfigureAwait(false);
        var answered = found.Select(one => one.Name).ToHashSet();
        var now = _time.GetUtcNow();
        foreach (var one in wanted)
        {
            _asked[one] = answered.Contains(one)
                ? new DnsWarmTry(now + DnsWarm.Rest(lifetime), 0)
                : Missed(one, now, lifetime);
        }

        foreach (var (name, address) in found)
        {
            _sets.Add(name.Rule, address, lifetime);
        }
    }

    private DnsWarmTry Missed(DnsWarmName name, DateTimeOffset now, TimeSpan lifetime)
    {
        var misses = (_asked.TryGetValue(name, out var last) ? last.Misses : 0) + 1;

        return new DnsWarmTry(now + DnsWarm.Again(misses, lifetime), misses);
    }

    private void Forget(IReadOnlyList<DnsWarmName> names)
    {
        if (_asked.Count <= names.Count)
        {
            return;
        }

        var held = names.ToHashSet();
        foreach (var gone in _asked.Keys.Where(name => !held.Contains(name)).ToArray())
        {
            _asked.Remove(gone);
        }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        try
        {
            await _sets.FlushAsync(_plans.Held, _settings.NameLifetime, ct).ConfigureAwait(false);
        }
        catch (HostNetworkException ex)
        {
            _logger.LogWarning(ex, "the answered addresses did not reach the sets of the rules");
        }
    }

    private async Task RestoreAsync(IServiceProvider services, CancellationToken ct)
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        var standings = await services.GetRequiredService<DnsStandingStore>().ListAsync(ct).ConfigureAwait(false);
        if (standings.Count == 0)
        {
            return;
        }

        _sets.Restore(standings, _settings.NameLifetime);
        _logger.LogInformation("{Count} addresses the rules stand on came back", standings.Count);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (now < _saving)
        {
            return;
        }

        _saving = now + Saving;
        var stirs = _sets.Stirs;
        if (stirs == _saved || _plans.Held is null)
        {
            return;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<DnsStandingStore>();
            await store.SaveAsync(_sets.Standings(_plans.Held), ct).ConfigureAwait(false);
            _saved = stirs;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "the addresses the rules stand on did not reach the panel database");
        }
    }

    private static async Task<IReadOnlyList<IPAddress>> AddressesAsync(
        IServiceProvider services,
        DnsSettings settings,
        CancellationToken ct)
    {
        if (settings.Listen.Count > 0)
        {
            return [.. settings.Listen.Select(one => DnsRules.Address(one, out var address) ? address : null).OfType<IPAddress>()];
        }

        var configs = await services.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false);

        return
        [
            .. configs
                .SelectMany(config => config.Address)
                .Select(Bare)
                .OfType<IPAddress>()
                .Distinct()
        ];
    }

    private static IPAddress? Bare(string range)
    {
        var slash = range.IndexOf('/', StringComparison.Ordinal);
        var text = slash < 0 ? range : range[..slash];

        return IPAddress.TryParse(text.Trim(), out var address) ? address : null;
    }

    /// <summary>
    /// When a name is asked about again and how many times it missed.
    /// </summary>
    private sealed record DnsWarmTry(DateTimeOffset Again, int Misses);
}
