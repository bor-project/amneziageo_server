namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Leaves the signals of the process to the panel.
/// </summary>
internal sealed class QuietLifetime : IHostLifetime
{
    /// <inheritdoc/>
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
