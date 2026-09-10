using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Api.Outbounds;

/// <summary>
/// Puts the outbounds and the rules the panel holds on the host when the server starts.
/// </summary>
public sealed class OutboundBoot : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;

    private readonly OutboundHost _host;

    private readonly ILogger<OutboundBoot> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public OutboundBoot(IServiceScopeFactory scopes, OutboundHost host, ILogger<OutboundBoot> logger)
    {
        _scopes = scopes;
        _host = host;
        _logger = logger;
    }

    /// <summary>
    /// Lays the outbounds once and the rules over them.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var services = scope.ServiceProvider;
            var outbounds = await services.GetRequiredService<OutboundStore>().ListAsync(stoppingToken)
                .ConfigureAwait(false);
            await LayAsync(outbounds, stoppingToken).ConfigureAwait(false);
            await services.GetRequiredService<RouteApplier>().SettleAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HostNetworkException or NetlinkException or InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "the outbounds and the rules stayed off the host");
        }
    }

    private async Task LayAsync(IReadOnlyList<OutboundConfig> outbounds, CancellationToken ct)
    {
        try
        {
            foreach (var state in await _host.SyncAsync(outbounds, ct).ConfigureAwait(false))
            {
                if (state.Fault.Length > 0)
                {
                    _logger.LogWarning("the host refused the outbound {Outbound}: {Reason}", state.Name, state.Fault);
                }
            }

            _logger.LogInformation("the host took {Count} outbounds", outbounds.Count(one => one.IsEnabled));
        }
        catch (HostNetworkException ex)
        {
            _logger.LogWarning(ex, "the outbounds stayed off the host");
        }
    }
}
