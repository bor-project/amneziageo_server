using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Names;

namespace AmneziaGeo.Server.Api.Names;

/// <summary>
/// Holds which endpoint the clients arrive on and where the panel may carry their connections, as the endpoints of
/// the panel stand.
/// </summary>
public sealed class NameInbounds
{
    private readonly IServiceScopeFactory _scopes;

    private readonly TimeProvider _time;

    private readonly ILogger<NameInbounds> _logger;

    private volatile NameReach _reach = NameReach.None;

    private long _renewed;

    private int _renewing;

    /// <summary>
    /// ctor
    /// </summary>
    public NameInbounds(IServiceScopeFactory scopes, TimeProvider time, ILogger<NameInbounds> logger)
    {
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// The reach as it was read last; a reach read long ago is read anew behind the caller.
    /// </summary>
    public NameReach Reach
    {
        get
        {
            if (_time.GetUtcNow().UtcTicks - Interlocked.Read(ref _renewed) >= NameDefaults.Renewing.Ticks
                && Interlocked.CompareExchange(ref _renewing, 1, 0) == 0)
            {
                _ = Task.Run(Again);
            }

            return _reach;
        }
    }

    /// <summary>
    /// Reads the endpoints of the panel anew.
    /// </summary>
    public async Task RenewAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var configs = await scope.ServiceProvider.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false);
            _reach = NameReach.Of(configs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "the endpoints of the clients were not read anew");
        }

        Interlocked.Exchange(ref _renewed, _time.GetUtcNow().UtcTicks);
    }

    private async Task Again()
    {
        try
        {
            await RenewAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _renewing, 0);
        }
    }
}
