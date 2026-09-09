using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Balancers;

/// <summary>
/// Reads back which outbounds carry traffic and lays the rules again when they change.
/// </summary>
public sealed class BalanceWatch : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;

    private readonly OutboundHost _host;

    private readonly BalanceLive _live;

    private readonly TimeProvider _time;

    private readonly ILogger<BalanceWatch> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public BalanceWatch(
        IServiceScopeFactory scopes,
        OutboundHost host,
        BalanceLive live,
        TimeProvider time,
        ILogger<BalanceWatch> logger)
    {
        _scopes = scopes;
        _host = host;
        _live = live;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Reads the outbounds once and lays the rules again when the live ones changed.
    /// </summary>
    public async Task LookAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var services = scope.ServiceProvider;
            var balancers = await services.GetRequiredService<BalanceStore>().ListAsync(ct).ConfigureAwait(false);
            if (balancers.Count == 0)
            {
                return;
            }

            var outbounds = await services.GetRequiredService<OutboundStore>().ListAsync(ct).ConfigureAwait(false);
            var alive = _host.States(outbounds).Where(state => state.Carries).Select(state => state.Name);
            if (!_live.Keep(alive))
            {
                return;
            }

            await services.GetRequiredService<RouteApplier>().SettleAsync(ct).ConfigureAwait(false);
            _logger.LogInformation(
                "the rules went on the host again: the balancers now pick from {Alive}",
                string.Join(", ", _live.Alive ?? new HashSet<string>()));
        }
        catch (Exception ex) when (ex is HostNetworkException or NetlinkException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "what the balancers pick from was not read back from the host");
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try
        {
            using var timer = new PeriodicTimer(BalanceDefaults.Watch, _time);
            do
            {
                await LookAsync(stopping).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stopping).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
