namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// Looks the releases of the panel over on its own.
/// </summary>
public sealed class UpdateSchedule : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(1);

    private readonly UpdateCenter _center;

    private readonly UpdateOptions _options;

    private readonly TimeProvider _time;

    private readonly ILogger<UpdateSchedule> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public UpdateSchedule(UpdateCenter center, UpdateOptions options, TimeProvider time, ILogger<UpdateSchedule> logger)
    {
        _center = center;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (_options.CheckHours <= 0)
        {
            _logger.LogInformation("the releases of the panel are looked for from the panel only");
            return;
        }

        try
        {
            await Task.Delay(FirstRun, _time, stopping).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.CheckHours), _time);
            do
            {
                await _center.CheckAsync(stopping).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stopping).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
