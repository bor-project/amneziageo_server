using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AmneziaGeo.Server.Geo.Files;
using AmneziaGeo.Server.Geo.Format;

namespace AmneziaGeo.Server.Geo;

/// <summary>
/// What one download of a source left.
/// </summary>
/// <param name="Changed">Whether the file on disk was replaced.</param>
/// <param name="AtUtc">When the download ran.</param>
/// <param name="Sha256">The digest of the file.</param>
/// <param name="EntryCount">How many countries or categories the file holds.</param>
/// <param name="Size">The size of the file, in bytes.</param>
/// <param name="ETag">The entity tag the next download asks against.</param>
/// <param name="LastModified">The modification time the next download asks against.</param>
public sealed record GeoDownload(
    bool Changed,
    DateTimeOffset AtUtc,
    string Sha256,
    int EntryCount,
    long Size,
    string ETag,
    string LastModified);

/// <summary>
/// Downloads geo databases and puts them in the file store.
/// </summary>
public sealed class GeoDownloader
{
    /// <summary>
    /// The largest database the server takes, in bytes.
    /// </summary>
    public const long MaxSize = 256L * 1024 * 1024;

    private static readonly TimeSpan ConnectLimit = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;

    private readonly IGeoFileStore _files;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public GeoDownloader(HttpClient http, IGeoFileStore files, TimeProvider? time = null)
    {
        _http = http;
        _files = files;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Downloads a source and stores it, keeping what is on disk when the server answers that nothing changed.
    /// </summary>
    public async Task<GeoDownload> UpdateAsync(GeoSource source, CancellationToken ct)
    {
        var fetched = await FetchAsync(source, ct).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        if (fetched is null)
        {
            return new GeoDownload(false, now, source.Sha256, source.EntryCount, source.Size, source.ETag, source.LastModified);
        }

        var (data, etag, lastModified) = fetched.Value;
        var count = Entries(source, data);

        await _files.WriteAsync(source.Name, data, ct).ConfigureAwait(false);

        return new GeoDownload(
            true,
            now,
            Convert.ToHexStringLower(SHA256.HashData(data)),
            count,
            data.LongLength,
            etag,
            lastModified);
    }

    // Reads the head of the file, so an address that answers with a page instead of a database is refused here.
    private static int Entries(GeoSource source, byte[] data)
    {
        int count;
        try
        {
            count = GeoKind.IsIp(source.Kind)
                ? GeoIpDatabase.Countries(data).Count
                : GeoSiteDatabase.Categories(data).Count;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or FormatException)
        {
            throw new InvalidDataException($"'{source.Url}' does not read as a {source.Kind} database", ex);
        }

        return count > 0
            ? count
            : throw new InvalidDataException($"'{source.Url}' carries no {source.Kind} entry");
    }

    private async Task<(byte[] Data, string ETag, string LastModified)?> FetchAsync(GeoSource source, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(ConnectLimit);

        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
        Ask(request, source);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var etag = response.Headers.ETag?.ToString() ?? string.Empty;
        if (source.Sha256.Length > 0 && etag.Length > 0 && string.Equals(source.ETag, etag, StringComparison.Ordinal))
        {
            return null;
        }

        var total = response.Content.Headers.ContentLength;
        if (total > MaxSize)
        {
            throw new InvalidDataException($"'{source.Url}' is larger than {MaxSize / (1024 * 1024)} megabytes");
        }

        var lastModified = response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

        limit.CancelAfter(StallLimit);
        var data = await ReadAsync(response, source.Url, total, limit).ConfigureAwait(false);

        return (data, etag, lastModified);
    }

    private static void Ask(HttpRequestMessage request, GeoSource source)
    {
        if (source.Sha256.Length == 0)
        {
            return;
        }

        if (source.ETag.Length > 0 && EntityTagHeaderValue.TryParse(source.ETag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        if (source.LastModified.Length > 0
            && DateTimeOffset.TryParse(source.LastModified, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var since))
        {
            request.Headers.IfModifiedSince = since;
        }
    }

    private static async Task<byte[]> ReadAsync(
        HttpResponseMessage response,
        string url,
        long? total,
        CancellationTokenSource limit)
    {
        using var source = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream(total is > 0 and < int.MaxValue ? (int)total : 0);

        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, limit.Token).ConfigureAwait(false)) > 0)
        {
            limit.CancelAfter(StallLimit);
            if (buffer.Length + read > MaxSize)
            {
                throw new InvalidDataException($"'{url}' is larger than {MaxSize / (1024 * 1024)} megabytes");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), limit.Token).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }
}
