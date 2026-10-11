using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// A release the panel can move to.
/// </summary>
/// <param name="Manifest">What the release carries.</param>
/// <param name="Files">The address the files of the release lie under.</param>
/// <param name="Notes">The page of the release.</param>
public sealed record UpdateOffer(UpdateManifest Manifest, Uri Files, string Notes);

/// <summary>
/// A release as a list of releases names it.
/// </summary>
/// <param name="Version">The version the tag of the release names, null for a manifest read on its own.</param>
/// <param name="Manifest">The address of the manifest.</param>
/// <param name="Signature">The address of the signature of the manifest.</param>
/// <param name="Notes">The page of the release.</param>
public sealed record UpdateListing(Version? Version, Uri Manifest, Uri Signature, string Notes);

/// <summary>
/// How long the look for a release waits for an answer.
/// </summary>
/// <param name="Attempt">The time one request is given.</param>
/// <param name="Whole">The time the whole look is given.</param>
/// <param name="Pause">The wait before a request is sent again.</param>
/// <param name="Tries">How many times one address is asked.</param>
public sealed record UpdatePatience(TimeSpan Attempt, TimeSpan Whole, TimeSpan Pause, int Tries)
{
    /// <summary>
    /// The waits of the panel.
    /// </summary>
    public static UpdatePatience Usual { get; } = new(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1), 5);
}

/// <summary>
/// Finds the newest release of the panel and reads its signed manifest.
/// </summary>
public sealed class UpdateFeed
{
    /// <summary>
    /// The file of a release that names what the release carries.
    /// </summary>
    public const string ManifestFile = "update.json";

    /// <summary>
    /// The file of a release that signs its manifest.
    /// </summary>
    public const string SignatureFile = "update.json.sig";

    /// <summary>
    /// How many of the newest releases are read when the builds before a release are taken.
    /// </summary>
    public const int Newest = 5;

    /// <summary>
    /// The largest answer about releases the panel reads, in bytes.
    /// </summary>
    public const int MaxList = 2 * 1024 * 1024;

    private readonly HttpClient _http;

    private readonly UpdateOptions _options;

    private readonly bool _tests;

    private readonly UpdatePatience _patience;

    /// <summary>
    /// ctor
    /// </summary>
    public UpdateFeed(HttpClient http, UpdateOptions options, bool tests)
        : this(http, options, tests, UpdatePatience.Usual)
    {
    }

    /// <summary>
    /// ctor
    /// </summary>
    public UpdateFeed(HttpClient http, UpdateOptions options, bool tests, UpdatePatience patience)
    {
        _http = http;
        _options = options;
        _tests = tests;
        _patience = patience;
    }

    /// <summary>
    /// Returns the newest release of the channel with its manifest checked against a key, or null when there is none.
    /// </summary>
    public async Task<UpdateOffer?> NewestAsync(string key, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var listing = _options.Manifest.Length > 0
            ? Direct(_options.Manifest)
            : Pick(await AskAsync(Address(_options.Repository, _tests), MaxList, list: true, clock, ct).ConfigureAwait(false), _tests);
        if (listing is null)
        {
            return null;
        }

        var manifest = await AskAsync(listing.Manifest, UpdateManifest.MaxSize, list: false, clock, ct).ConfigureAwait(false);
        var signature = await AskAsync(listing.Signature, UpdateSignature.MaxSize, list: false, clock, ct).ConfigureAwait(false);
        if (!UpdateSignature.Holds(manifest, signature, key))
        {
            throw new InvalidDataException($"the manifest at {listing.Manifest} is not signed with the key of the releases");
        }

        var parsed = UpdateManifest.Parse(manifest);
        if (listing.Version is not null && parsed.Version != listing.Version)
        {
            throw new InvalidDataException($"the release {listing.Version} carries the manifest of {parsed.Version}");
        }

        if (!_tests && !string.Equals(parsed.Channel, UpdateOptions.Stable, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new UpdateOffer(parsed, new Uri(listing.Manifest, "."), listing.Notes);
    }

    /// <summary>
    /// Returns the address GitHub is asked at: the latest release alone, or the newest ones when the builds before a
    /// release are taken.
    /// </summary>
    public static Uri Address(string repository, bool tests) =>
        new(tests
            ? $"https://api.github.com/repos/{repository}/releases?per_page={Newest}"
            : $"https://api.github.com/repos/{repository}/releases/latest");

    /// <summary>
    /// Returns the newest release that carries a signed manifest, out of a list of releases of GitHub or of one release.
    /// </summary>
    public static UpdateListing? Pick(byte[] releases, bool tests)
    {
        using var document = JsonDocument.Parse(releases);
        if (document.RootElement.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
        {
            throw new InvalidDataException("the releases read neither as a JSON array nor as one release");
        }

        var best = default(UpdateListing);
        foreach (var release in Each(document.RootElement))
        {
            if (Flag(release, "draft") || (!tests && Flag(release, "prerelease")))
            {
                continue;
            }

            var version = VersionOf(Text(release, "tag_name"));
            var manifest = Asset(release, ManifestFile);
            var signature = Asset(release, SignatureFile);
            if (version is null || manifest is null || signature is null)
            {
                continue;
            }

            if (best?.Version is null || version > best.Version)
            {
                best = new UpdateListing(version, manifest, signature, Text(release, "html_url"));
            }
        }

        return best;
    }

    /// <summary>
    /// Reads the version out of the tag of a release, or null when the tag names none.
    /// </summary>
    public static Version? VersionOf(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        var bare = tag.StartsWith('v') ? tag[1..] : tag;
        var cut = bare.IndexOf('-', StringComparison.Ordinal);

        return Version.TryParse(cut >= 0 ? bare[..cut] : bare, out var version) ? version : null;
    }

    private static UpdateListing Direct(string address)
    {
        var manifest = new Uri(address, UriKind.Absolute);

        return new UpdateListing(null, manifest, new Uri(manifest.AbsoluteUri + ".sig"), string.Empty);
    }

    // Reads an address, asking again while the look has time and tries left.
    private async Task<byte[]> AskAsync(Uri address, int max, bool list, Stopwatch clock, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var left = _patience.Whole - clock.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                throw new TimeoutException($"the look for a release ran out of its {(int)_patience.Whole.TotalSeconds} s before {address.Host} answered");
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(left < _patience.Attempt ? left : _patience.Attempt);
            try
            {
                return await ReadAsync(address, max, list, limit.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (Passes(ex, ct) && attempt < _patience.Tries && clock.Elapsed + _patience.Pause < _patience.Whole)
            {
                await Task.Delay(_patience.Pause, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw Silence(address, clock, attempt);
            }
        }
    }

    // Tells whether a failure may pass by itself: no answer in time, a broken connection, a fault of the far side.
    private static bool Passes(Exception failure, CancellationToken ct) =>
        failure switch
        {
            OperationCanceledException => !ct.IsCancellationRequested,
            HttpRequestException refusal => refusal.StatusCode is null || (int)refusal.StatusCode >= 500,
            IOException => true,
            _ => false,
        };

    // Words the failure of a host that did not answer in the time of the look.
    private static TimeoutException Silence(Uri address, Stopwatch clock, int tries) =>
        new($"{address.Host} did not answer in {(int)Math.Round(clock.Elapsed.TotalSeconds)} s, asked {tries} time(s)");

    private async Task<byte[]> ReadAsync(Uri address, int max, bool list, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        if (list)
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.AcceptEncoding.ParseAdd("gzip");
        }

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{address} answered {(int)response.StatusCode}", null, response.StatusCode);
        }

        if (response.Content.Headers.ContentLength > max)
        {
            throw new InvalidDataException($"{address} is larger than {max} bytes");
        }

        var packed = response.Content.Headers.ContentEncoding.Contains("gzip", StringComparer.OrdinalIgnoreCase);
        using var answer = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var source = packed ? new GZipStream(answer, CompressionMode.Decompress) : answer;
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        var read = 0;
        while ((read = await source.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > max)
            {
                throw new InvalidDataException($"{address} is larger than {max} bytes");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    // Returns the releases an answer of GitHub holds: the items of a list, or the one release it is.
    private static IEnumerable<JsonElement> Each(JsonElement releases)
    {
        if (releases.ValueKind == JsonValueKind.Object)
        {
            yield return releases;
            yield break;
        }

        foreach (var release in releases.EnumerateArray())
        {
            yield return release;
        }
    }

    private static Uri? Asset(JsonElement release, string name)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (string.Equals(Text(asset, "name"), name, StringComparison.Ordinal)
                && Uri.TryCreate(Text(asset, "browser_download_url"), UriKind.Absolute, out var address)
                && address.Scheme == Uri.UriSchemeHttps)
            {
                return address;
            }
        }

        return null;
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
