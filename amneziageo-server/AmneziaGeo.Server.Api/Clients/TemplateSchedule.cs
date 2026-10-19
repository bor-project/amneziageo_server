using System.Threading.Channels;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// Works the ranges of the client templates out anew on its own: the ones a day old, and all of them after the geo
/// databases change.
/// </summary>
public sealed class TemplateSchedule : BackgroundService
{
    /// <summary>
    /// How old the ranges of a template grow before they are worked out anew.
    /// </summary>
    public static readonly TimeSpan Age = TimeSpan.FromDays(1);

    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan Between = TimeSpan.FromHours(1);

    private readonly Channel<bool> _bell = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly IServiceScopeFactory _scopes;

    private readonly TimeProvider _time;

    private readonly ILogger<TemplateSchedule> _logger;

    private int _all;

    /// <summary>
    /// ctor
    /// </summary>
    public TemplateSchedule(IServiceScopeFactory scopes, TimeProvider time, ILogger<TemplateSchedule> logger)
    {
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Has every template with entries worked out anew without waiting for its age.
    /// </summary>
    public void Wake()
    {
        Volatile.Write(ref _all, 1);
        _bell.Writer.TryWrite(true);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try
        {
            await Task.Delay(FirstRun, _time, stopping).ConfigureAwait(false);
            while (true)
            {
                _bell.Reader.TryRead(out _);
                var all = Interlocked.Exchange(ref _all, 0) == 1;
                await RunAsync(all ? null : Age, stopping).ConfigureAwait(false);
                await RestAsync(stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunAsync(TimeSpan? olderThan, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var renewer = scope.ServiceProvider.GetRequiredService<TemplateRenewer>();
            await renewer.RenewAsync(olderThan, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "the ranges of the templates were not worked out anew");
        }
    }

    // Waits for the next hour or for a wake, whichever comes first.
    private async Task RestAsync(CancellationToken stopping)
    {
        using var rest = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var hour = Task.Delay(Between, _time, rest.Token);
        var bell = _bell.Reader.WaitToReadAsync(rest.Token).AsTask();
        await Task.WhenAny(hour, bell).ConfigureAwait(false);
        await rest.CancelAsync().ConfigureAwait(false);
        stopping.ThrowIfCancellationRequested();
    }
}
