using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// Puts the clients the panel holds on the interfaces of the endpoints when the server starts.
/// </summary>
public sealed class ClientBoot : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;

    private readonly ClientHost _host;

    private readonly ILogger<ClientBoot> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public ClientBoot(IServiceScopeFactory scopes, ClientHost host, ILogger<ClientBoot> logger)
    {
        _scopes = scopes;
        _host = host;
        _logger = logger;
    }

    /// <summary>
    /// Walks the endpoints once and puts their clients on the host.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopes.CreateScope();
        var configs = scope.ServiceProvider.GetRequiredService<ConfigStore>();
        var store = scope.ServiceProvider.GetRequiredService<ClientStore>();

        foreach (var endpoint in await configs.ListAsync(stoppingToken).ConfigureAwait(false))
        {
            var clients = await store.ListAsync(endpoint.Id, stoppingToken).ConfigureAwait(false);
            if (clients.Count == 0)
            {
                continue;
            }

            var sync = await _host.SyncAsync(endpoint, clients, [], stoppingToken).ConfigureAwait(false);
            if (sync.IsDone)
            {
                _logger.LogInformation(
                    "the interface {Endpoint} took {Count} clients",
                    endpoint.Name,
                    clients.Count(client => client.IsEnabled));

                continue;
            }

            _logger.LogWarning(
                "the clients of {Endpoint} stayed off the interface: {Reason}",
                endpoint.Name,
                sync.Message);
        }
    }
}
