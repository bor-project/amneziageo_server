using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// Runs the resolver and carries the answered addresses to the sets of the rules.
/// </summary>
public sealed class DnsHost : BackgroundService
{
    private static readonly TimeSpan Between = TimeSpan.FromMilliseconds(500);

    private readonly SemaphoreSlim _turn = new(1, 1);

    private readonly IServiceScopeFactory _scopes;

    private readonly RoutePlans _plans;

    private readonly DnsSets _sets;

    private readonly DnsState _state;

    private readonly TimeProvider _time;

    private readonly ILogger<DnsHost> _logger;

    private DnsServer? _server;

    private DnsSettings _settings = DnsDefaults.Settings;

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
    /// Takes the settings anew and starts the resolver over.
    /// </summary>
    public Task RestartAsync(CancellationToken ct) => StartAsync(true, ct);

    /// <summary>
    /// Starts the resolver over on the addresses of the configurations with the settings it runs with.
    /// </summary>
    public Task RebindAsync(CancellationToken ct) => StartAsync(false, ct);

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken ct)
    {
        _server?.Stop();
        _server = null;
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
                await FlushAsync(stopping).ConfigureAwait(false);
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
        var resolver = new DnsResolver(
            new DnsUpstream(_settings.Upstreams, DnsDefaults.Wait),
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
        _state.Started([.. taken.Select(address => address.ToString())], _time.GetUtcNow());
        _logger.LogInformation("the resolver answers on {Addresses} port {Port}", string.Join(", ", _state.Listening), _settings.Port);
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
}
