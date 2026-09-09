using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Probe;

namespace AmneziaGeo.Server.Api.Outbounds;

/// <summary>
/// Probes the outbounds that are due one and lays the rules again when a verdict changes.
/// </summary>
public sealed class ProbeWatch : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;

    private readonly ProbeRunner _runner;

    private readonly ProbeLive _live;

    private readonly OutboundHost _host;

    private readonly BalanceLive _carrying;

    private readonly TimeProvider _time;

    private readonly ILogger<ProbeWatch> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public ProbeWatch(
        IServiceScopeFactory scopes,
        ProbeRunner runner,
        ProbeLive live,
        OutboundHost host,
        BalanceLive carrying,
        TimeProvider time,
        ILogger<ProbeWatch> logger)
    {
        _scopes = scopes;
        _runner = runner;
        _live = live;
        _host = host;
        _carrying = carrying;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Probes every outbound that is due one.
    /// </summary>
    public async Task LookAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var services = scope.ServiceProvider;
            var outbounds = await services.GetRequiredService<OutboundStore>().ListAsync(ct).ConfigureAwait(false);
            _live.Hold(outbounds.Select(one => one.Name));
            var moved = false;
            foreach (var outbound in outbounds.Where(_runner.IsDue))
            {
                moved |= await _runner.RunAsync(outbound, ct).ConfigureAwait(false);
            }

            if (!moved)
            {
                return;
            }

            _carrying.Keep(_host.States(outbounds).Where(state => state.Carries).Select(state => state.Name));
            await services.GetRequiredService<RouteApplier>().SettleAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "the outbounds were not probed");
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try
        {
            using var timer = new PeriodicTimer(ProbeDefaults.Tick, _time);
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
