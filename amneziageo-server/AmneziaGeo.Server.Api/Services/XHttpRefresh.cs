namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Refreshes XHTTP authorization and processes.
/// </summary>
public sealed class XHttpRefresh : BackgroundService
{
    private readonly ServiceServer _server;
    private readonly AmneziaGeo.Server.Routing.Udp.UdpPorts? _ports;

    private readonly ILogger<XHttpRefresh> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public XHttpRefresh(ServiceServer server, ILogger<XHttpRefresh> logger, AmneziaGeo.Server.Routing.Udp.UdpPorts? ports = null)
    {
        _server = server;
        _ports = ports;
        _logger = logger;
    }

    /// <summary>
    /// Applies current client credentials.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var ports = _ports;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var hold = ports is null ? null : await ports.HoldAsync(stoppingToken).ConfigureAwait(false);
                await _server.RefreshXHttpAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "the XHTTP processes were not refreshed");
            }
        }
    }
}
