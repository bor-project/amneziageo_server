using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;

namespace AmneziaGeo.Server.Api.Geo;

/// <summary>
/// The sources a round of downloads leaves and whether it replaced a file.
/// </summary>
/// <param name="Sources">Every source as it now stands.</param>
/// <param name="Changed">Whether a file on disk was replaced.</param>
public sealed record GeoRound(IReadOnlyList<GeoSource> Sources, bool Changed);

/// <summary>
/// Downloads a geo source and writes down what the download left.
/// </summary>
public sealed class GeoRefresher
{
    private readonly GeoStore _store;

    private readonly GeoDownloader _downloader;

    private readonly ILogger<GeoRefresher> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public GeoRefresher(GeoStore store, GeoDownloader downloader, ILogger<GeoRefresher> logger)
    {
        _store = store;
        _downloader = downloader;
        _logger = logger;
    }

    /// <summary>
    /// Downloads one source and returns it as it now stands.
    /// </summary>
    public async Task<GeoSource> RefreshAsync(GeoSource source, CancellationToken ct) =>
        (await DownloadAsync(source, ct).ConfigureAwait(false)).Source;

    /// <summary>
    /// Downloads every source that is on and due, and returns them all.
    /// </summary>
    public async Task<IReadOnlyList<GeoSource>> RefreshAllAsync(TimeSpan? notNewerThan, DateTimeOffset now, CancellationToken ct) =>
        (await RoundAsync(notNewerThan, now, ct).ConfigureAwait(false)).Sources;

    /// <summary>
    /// Downloads every source that is on and due, and returns them all with whether a file was replaced.
    /// </summary>
    public async Task<GeoRound> RoundAsync(TimeSpan? notNewerThan, DateTimeOffset now, CancellationToken ct)
    {
        var held = await _store.ListAsync(ct).ConfigureAwait(false);
        var answer = new List<GeoSource>(held.Count);
        var changed = false;

        foreach (var source in held)
        {
            if (!Due(source, notNewerThan, now))
            {
                answer.Add(source);
                continue;
            }

            var (fresh, replaced) = await DownloadAsync(source, ct).ConfigureAwait(false);
            answer.Add(fresh);
            changed |= replaced;
        }

        return new GeoRound(answer, changed);
    }

    // Downloads one source, writes down what the download left and tells whether the file was replaced.
    private async Task<(GeoSource Source, bool Changed)> DownloadAsync(GeoSource source, CancellationToken ct)
    {
        try
        {
            var download = await _downloader.UpdateAsync(source, ct).ConfigureAwait(false);
            var stamped = await _store.StampAsync(source.Id, download, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "geo source {Name} {State}, {Count} entries",
                source.Name,
                download.Changed ? "downloaded" : "unchanged",
                download.EntryCount);

            return (stamped.Record ?? source, download.Changed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "geo source {Name} was not downloaded", source.Name);
            var failed = await _store.FailAsync(source.Id, ex.Message, ct).ConfigureAwait(false);

            return (failed.Record ?? source, false);
        }
    }

    private static bool Due(GeoSource source, TimeSpan? notNewerThan, DateTimeOffset now)
    {
        if (!source.IsEnabled)
        {
            return false;
        }

        return notNewerThan is null || source.UpdatedUtc is not { } stamp || now - stamp >= notNewerThan;
    }
}
