using AmneziaGeo.Server.Api.Rules;

namespace AmneziaGeo.Server.Api.Geo;

/// <summary>
/// Downloads the geo sources that fell behind, on its own.
/// </summary>
public sealed class GeoSchedule : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan Between = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopes;

    private readonly GeoOptions _options;

    private readonly TimeProvider _time;

    private readonly ILogger<GeoSchedule> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public GeoSchedule(IServiceScopeFactory scopes, GeoOptions options, TimeProvider time, ILogger<GeoSchedule> logger)
    {
        _scopes = scopes;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (_options.UpdateHours <= 0)
        {
            _logger.LogInformation("geo sources are downloaded from the panel only");
            return;
        }

        var age = TimeSpan.FromHours(_options.UpdateHours);

        try
        {
            await Task.Delay(FirstRun, _time, stopping).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Between, _time);
            do
            {
                await RunAsync(age, stopping).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stopping).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunAsync(TimeSpan age, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var refresher = scope.ServiceProvider.GetRequiredService<GeoRefresher>();
            await refresher.RefreshAllAsync(age, _time.GetUtcNow(), ct).ConfigureAwait(false);
            await scope.ServiceProvider.GetRequiredService<RouteApplier>().SettleAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "the geo sources were not gone over");
        }
    }
}
