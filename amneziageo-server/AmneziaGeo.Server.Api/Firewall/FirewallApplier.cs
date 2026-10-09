using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Firewall;

namespace AmneziaGeo.Server.Api.Firewall;

/// <summary>
/// Holds the ports the panel keeps open in the firewall of the host.
/// </summary>
public sealed class FirewallApplier
{
    private readonly ConfigStore _configs;

    private readonly PanelStore _panel;

    private readonly SubscriptionStore _subscriptions;

    private readonly DnsStore _dns;

    private readonly DnsState _resolver;

    private readonly FirewallHost _host;

    private readonly ILogger<FirewallApplier> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public FirewallApplier(
        ConfigStore configs,
        PanelStore panel,
        SubscriptionStore subscriptions,
        DnsStore dns,
        DnsState resolver,
        FirewallHost host,
        ILogger<FirewallApplier> logger)
    {
        _configs = configs;
        _panel = panel;
        _subscriptions = subscriptions;
        _dns = dns;
        _resolver = resolver;
        _host = host;
        _logger = logger;
    }

    /// <summary>
    /// Returns what the panel holds open in the firewall of the host as the endpoints and the settings stand now,
    /// the resolver as it runs.
    /// </summary>
    public async Task<FirewallPlan> PlanAsync(CancellationToken ct) =>
        FirewallPlan.Of(
            await _configs.ListAsync(ct).ConfigureAwait(false),
            await _panel.ReadAsync(ct).ConfigureAwait(false),
            await _subscriptions.ReadAsync(ct).ConfigureAwait(false),
            _resolver.Settings ?? await _dns.ReadAsync(ct).ConfigureAwait(false));

    /// <summary>
    /// Opens the ports the panel keeps open and closes the rest of what it opened before.
    /// </summary>
    public Task<FirewallSync> SettleAsync(CancellationToken ct) => SettleAsync(null, ct);

    /// <summary>
    /// Opens the ports the panel keeps open and closes the rest of what it opened before, except the ports the plan
    /// taken before a change held and the panel holds no longer: those are handed over to the host and stay open. A
    /// port the host did not take over stays open under the panel until the next settle.
    /// </summary>
    public async Task<FirewallSync> SettleAsync(FirewallPlan? before, CancellationToken ct)
    {
        var plan = await PlanAsync(ct).ConfigureAwait(false);
        var dropped = before is null ? [] : FirewallPlan.Dropped(before, plan);
        var kept = await _host.KeepAsync(dropped, ct).ConfigureAwait(false);
        var applied = await _host.ApplyAsync(kept.IsDone ? plan : plan with { Ports = [.. plan.Ports, .. dropped] }, ct)
            .ConfigureAwait(false);
        var sync = kept.IsDone ? applied : kept;
        if (!sync.IsDone)
        {
            _logger.LogWarning("{Engine} refused the ports of the panel: {Reason}", sync.Engine, sync.Message);
        }

        return sync;
    }
}
