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

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;

    private readonly UpdateOptions _options;

    /// <summary>
    /// ctor
    /// </summary>
    public UpdateFeed(HttpClient http, UpdateOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <summary>
    /// Returns the newest release of the channel with its manifest checked against a key, or null when there is none.
    /// </summary>
    public async Task<UpdateOffer?> NewestAsync(string key, CancellationToken ct)
    {
        var listing = _options.Manifest.Length > 0
            ? Direct(_options.Manifest)
            : await ListAsync(ct).ConfigureAwait(false);
        if (listing is null)
        {
            return null;
        }

        var manifest = await ReadAsync(listing.Manifest, UpdateManifest.MaxSize, ct).ConfigureAwait(false);
        var signature = await ReadAsync(listing.Signature, UpdateSignature.MaxSize, ct).ConfigureAwait(false);
        if (!UpdateSignature.Holds(manifest, signature, key))
        {
            throw new InvalidDataException($"the manifest at {listing.Manifest} is not signed with the key of the releases");
        }

        var parsed = UpdateManifest.Parse(manifest);
        if (listing.Version is not null && parsed.Version != listing.Version)
        {
            throw new InvalidDataException($"the release {listing.Version} carries the manifest of {parsed.Version}");
        }

        if (!_options.TakesTests && !string.Equals(parsed.Channel, UpdateOptions.Stable, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new UpdateOffer(parsed, new Uri(listing.Manifest, "."), listing.Notes);
    }

    /// <summary>
    /// Returns the newest release in a list of releases of GitHub that carries a signed manifest.
    /// </summary>
    public static UpdateListing? Pick(byte[] releases, bool tests)
    {
        using var document = JsonDocument.Parse(releases);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("the list of releases is not a JSON array");
        }

        var best = default(UpdateListing);
        foreach (var release in document.RootElement.EnumerateArray())
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

    private async Task<UpdateListing?> ListAsync(CancellationToken ct)
    {
        var address = new Uri($"https://api.github.com/repos/{_options.Repository}/releases?per_page=30");
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Limit);
        using var response = await _http.SendAsync(request, limit.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{address} answered {(int)response.StatusCode}");
        }

        var body = await response.Content.ReadAsByteArrayAsync(limit.Token).ConfigureAwait(false);

        return Pick(body, _options.TakesTests);
    }

    private async Task<byte[]> ReadAsync(Uri address, int max, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Limit);
        using var response = await _http
            .GetAsync(address, HttpCompletionOption.ResponseHeadersRead, limit.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{address} answered {(int)response.StatusCode}");
        }

        if (response.Content.Headers.ContentLength > max)
        {
            throw new InvalidDataException($"{address} is larger than {max} bytes");
        }

        using var source = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        var read = 0;
        while ((read = await source.ReadAsync(chunk, limit.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > max)
            {
                throw new InvalidDataException($"{address} is larger than {max} bytes");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
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
