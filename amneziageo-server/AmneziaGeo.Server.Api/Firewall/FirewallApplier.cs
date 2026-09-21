using AmneziaGeo.Server.Dal;
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

    private readonly FirewallHost _host;

    private readonly ILogger<FirewallApplier> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public FirewallApplier(
        ConfigStore configs,
        PanelStore panel,
        SubscriptionStore subscriptions,
        FirewallHost host,
        ILogger<FirewallApplier> logger)
    {
        _configs = configs;
        _panel = panel;
        _subscriptions = subscriptions;
        _host = host;
        _logger = logger;
    }

    /// <summary>
    /// Opens the ports the panel keeps open and closes the rest of what it opened before.
    /// </summary>
    public async Task<FirewallSync> SettleAsync(CancellationToken ct)
    {
        var plan = FirewallPlan.Of(
            await _configs.ListAsync(ct).ConfigureAwait(false),
            await _panel.ReadAsync(ct).ConfigureAwait(false),
            await _subscriptions.ReadAsync(ct).ConfigureAwait(false));
        var sync = await _host.ApplyAsync(plan, ct).ConfigureAwait(false);
        if (!sync.IsDone)
        {
            _logger.LogWarning("{Engine} refused the ports of the panel: {Reason}", sync.Engine, sync.Message);
        }

        return sync;
    }
}
