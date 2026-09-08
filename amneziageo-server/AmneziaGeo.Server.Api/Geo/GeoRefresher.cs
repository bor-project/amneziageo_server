using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;

namespace AmneziaGeo.Server.Api.Geo;

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
    public async Task<GeoSource> RefreshAsync(GeoSource source, CancellationToken ct)
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

            return stamped.Record ?? source;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "geo source {Name} was not downloaded", source.Name);
            var failed = await _store.FailAsync(source.Id, ex.Message, ct).ConfigureAwait(false);

            return failed.Record ?? source;
        }
    }

    /// <summary>
    /// Downloads every source that is on and due, and returns them all.
    /// </summary>
    public async Task<IReadOnlyList<GeoSource>> RefreshAllAsync(TimeSpan? notNewerThan, DateTimeOffset now, CancellationToken ct)
    {
        var held = await _store.ListAsync(ct).ConfigureAwait(false);
        var answer = new List<GeoSource>(held.Count);

        foreach (var source in held)
        {
            answer.Add(Due(source, notNewerThan, now)
                ? await RefreshAsync(source, ct).ConfigureAwait(false)
                : source);
        }

        return answer;
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
