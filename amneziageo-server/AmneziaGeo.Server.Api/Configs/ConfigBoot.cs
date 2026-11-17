using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// Raises the interfaces of the endpoints the panel holds when the server starts.
/// </summary>
public sealed class ConfigBoot : IHostedService
{
    private readonly IServiceScopeFactory _scopes;

    private readonly EndpointHost _host;

    private readonly SharedUdpHost? _udp;

    private readonly ILogger<ConfigBoot> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public ConfigBoot(IServiceScopeFactory scopes, EndpointHost host, ILogger<ConfigBoot> logger, SharedUdpHost? udp = null)
    {
        _scopes = scopes;
        _host = host;
        _udp = udp;
        _logger = logger;
    }

    /// <summary>
    /// Puts every endpoint on the host before the clients are laid on top of them.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ConfigStore>();
        var configs = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        if (_udp is not null)
        {
            var proxies = await scope.ServiceProvider.GetRequiredService<TransportProxyStore>().ListAsync(cancellationToken).ConfigureAwait(false);
            var fronts = scope.ServiceProvider.GetService<FrontOptions>()?.Enabled != false;
            try
            {
                await _udp.PrepareAsync(configs, proxies.Where(one => fronts && one.IsEnabled && one.Hosted && one.Transport == "quic")
                    .Select(one => one.ListenPort == 0 ? one.Port : one.ListenPort).ToHashSet(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "The shared UDP listeners could not start");
            }
        }

        if (configs.Count == 0)
        {
            return;
        }

        var clients = await scope.ServiceProvider.GetRequiredService<ClientStore>()
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var sync in await _host.SyncAsync(configs, clients, cancellationToken).ConfigureAwait(false))
        {
            if (sync.IsDone)
            {
                _logger.LogInformation("the host took {Endpoint}", sync.Name);

                continue;
            }

            _logger.LogWarning("the host refused {Endpoint}: {Reason}", sync.Name, sync.Message);
        }
    }

    /// <summary>
    /// Leaves the interfaces where they are when the server stops.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
