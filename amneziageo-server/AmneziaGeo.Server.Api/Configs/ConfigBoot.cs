using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// Raises the interfaces of the endpoints the panel holds when the server starts.
/// </summary>
public sealed class ConfigBoot : IHostedService
{
    private readonly IServiceScopeFactory _scopes;

    private readonly EndpointHost _host;

    private readonly ILogger<ConfigBoot> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public ConfigBoot(IServiceScopeFactory scopes, EndpointHost host, ILogger<ConfigBoot> logger)
    {
        _scopes = scopes;
        _host = host;
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
