using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AmneziaGeo.Server.Updates;

namespace AmneziaGeo.Server.Api.ClientUpdates;

/// <summary>
/// The releases of the client as the panel reaches them: in its own store or in a service that stands apart.
/// </summary>
public interface IReleaseHub
{
    /// <summary>
    /// Returns what the store says about itself.
    /// </summary>
    Task<StoreStatus> StatusAsync(CancellationToken ct);

    /// <summary>
    /// Returns one page of the releases, the newest first.
    /// </summary>
    Task<ReleasePage> ListAsync(int page, int size, CancellationToken ct);

    /// <summary>
    /// Returns a release with its files.
    /// </summary>
    Task<ReleaseDetails> ReadAsync(string tag, CancellationToken ct);

    /// <summary>
    /// Makes a release.
    /// </summary>
    Task<ReleaseDetails> CreateAsync(ReleaseDraft draft, CancellationToken ct);

    /// <summary>
    /// Changes what a release says about itself.
    /// </summary>
    Task<ReleaseDetails> ChangeAsync(string tag, ReleaseChange change, CancellationToken ct);

    /// <summary>
    /// Removes a release with its files.
    /// </summary>
    Task DeleteAsync(string tag, CancellationToken ct);

    /// <summary>
    /// Takes a file into a release.
    /// </summary>
    Task<ReleaseDetails> PutFileAsync(string tag, string name, Stream body, string? from, CancellationToken ct);

    /// <summary>
    /// Takes a file out of a release.
    /// </summary>
    Task<ReleaseDetails> DropFileAsync(string tag, string name, CancellationToken ct);

    /// <summary>
    /// Takes the files of a release out of a zip archive.
    /// </summary>
    Task<ReleaseDetails> ImportAsync(string tag, Stream zip, CancellationToken ct);
}

/// <summary>
/// The releases in the store of the panel.
/// </summary>
public sealed class LocalReleaseHub : IReleaseHub
{
    private readonly ReleaseStore _store;

    /// <summary>
    /// ctor
    /// </summary>
    public LocalReleaseHub(ReleaseStore store)
    {
        _store = store;
    }

    /// <inheritdoc/>
    public Task<StoreStatus> StatusAsync(CancellationToken ct) => Task.FromResult(_store.Status());

    /// <inheritdoc/>
    public Task<ReleasePage> ListAsync(int page, int size, CancellationToken ct) => Task.FromResult(_store.List(page, size));

    /// <inheritdoc/>
    public Task<ReleaseDetails> ReadAsync(string tag, CancellationToken ct) => Task.FromResult(_store.Read(tag));

    /// <inheritdoc/>
    public Task<ReleaseDetails> CreateAsync(ReleaseDraft draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return _store.CreateAsync(draft.Version, draft.Channel, draft.Description, ct);
    }

    /// <inheritdoc/>
    public Task<ReleaseDetails> ChangeAsync(string tag, ReleaseChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);

        return _store.ChangeAsync(tag, change.Channel, change.Description, change.From, ct);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string tag, CancellationToken ct) => _store.DeleteAsync(tag, ct);

    /// <inheritdoc/>
    public Task<ReleaseDetails> PutFileAsync(string tag, string name, Stream body, string? from, CancellationToken ct) =>
        _store.PutFileAsync(tag, name, body, from, ct);

    /// <inheritdoc/>
    public Task<ReleaseDetails> DropFileAsync(string tag, string name, CancellationToken ct) => _store.DropFileAsync(tag, name, ct);

    /// <inheritdoc/>
    public Task<ReleaseDetails> ImportAsync(string tag, Stream zip, CancellationToken ct) => _store.ImportAsync(tag, zip, ct);
}

/// <summary>
/// The releases in a service that stands apart, reached over its API with the token.
/// </summary>
public sealed class RemoteReleaseHub : IReleaseHub
{
    /// <summary>
    /// The code the panel refuses with when the service apart does not answer as one.
    /// </summary>
    public const string Unreached = "updates-address";

    /// <summary>
    /// The code of a refusal of the token by the service apart.
    /// </summary>
    public const string Untrusted = "updates-token";

    private readonly HttpClient _http;

    /// <summary>
    /// ctor
    /// </summary>
    public RemoteReleaseHub(HttpClient http)
    {
        _http = http;
    }

    /// <inheritdoc/>
    public Task<StoreStatus> StatusAsync(CancellationToken ct) => SendAsync<StoreStatus>(HttpMethod.Get, "status", null, ct);

    /// <inheritdoc/>
    public Task<ReleasePage> ListAsync(int page, int size, CancellationToken ct) =>
        SendAsync<ReleasePage>(HttpMethod.Get, $"releases?page={page}&size={size}", null, ct);

    /// <inheritdoc/>
    public Task<ReleaseDetails> ReadAsync(string tag, CancellationToken ct) =>
        SendAsync<ReleaseDetails>(HttpMethod.Get, "releases/" + Uri.EscapeDataString(tag), null, ct);

    /// <inheritdoc/>
    public Task<ReleaseDetails> CreateAsync(ReleaseDraft draft, CancellationToken ct) =>
        SendAsync<ReleaseDetails>(HttpMethod.Post, "releases", JsonContent.Create(draft), ct);

    /// <inheritdoc/>
    public Task<ReleaseDetails> ChangeAsync(string tag, ReleaseChange change, CancellationToken ct) =>
        SendAsync<ReleaseDetails>(HttpMethod.Patch, "releases/" + Uri.EscapeDataString(tag), JsonContent.Create(change), ct);

    /// <inheritdoc/>
    public async Task DeleteAsync(string tag, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "releases/" + Uri.EscapeDataString(tag));
        using var response = await AskAsync(request, ct).ConfigureAwait(false);
        await CheckAsync(response, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<ReleaseDetails> PutFileAsync(string tag, string name, Stream body, string? from, CancellationToken ct)
    {
        var path = $"releases/{Uri.EscapeDataString(tag)}/files/{Uri.EscapeDataString(name)}";
        if (!string.IsNullOrWhiteSpace(from))
        {
            path += "?from=" + Uri.EscapeDataString(from);
        }

        return SendAsync<ReleaseDetails>(HttpMethod.Put, path, new StreamContent(body), ct);
    }

    /// <inheritdoc/>
    public Task<ReleaseDetails> DropFileAsync(string tag, string name, CancellationToken ct) =>
        SendAsync<ReleaseDetails>(HttpMethod.Delete, $"releases/{Uri.EscapeDataString(tag)}/files/{Uri.EscapeDataString(name)}", null, ct);

    /// <inheritdoc/>
    public Task<ReleaseDetails> ImportAsync(string tag, Stream zip, CancellationToken ct) =>
        SendAsync<ReleaseDetails>(HttpMethod.Post, $"releases/{Uri.EscapeDataString(tag)}/archive", new StreamContent(zip), ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await AskAsync(request, ct).ConfigureAwait(false);
        await CheckAsync(response, ct).ConfigureAwait(false);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct).ConfigureAwait(false)
                ?? throw new ReleaseRefused(Unreached, "the service answered with nothing", 502);
        }
        catch (JsonException ex)
        {
            throw new ReleaseRefused(Unreached, "the service did not answer as one of the releases: " + ex.Message, 502);
        }
    }

    private async Task<HttpResponseMessage> AskAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ReleaseRefused(Unreached, "the service did not answer: " + ex.Message, 502);
        }
    }

    // Turns a refusal of the service into one of the panel.
    private static async Task CheckAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var failure = default(UpdateFailure);
        try
        {
            failure = await response.Content.ReadFromJsonAsync<UpdateFailure>(ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // The service answered with something other than a refusal of the releases.
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ReleaseRefused(Untrusted, "the service did not take the token", 502);
        }

        throw failure is { Error.Length: > 0 }
            ? new ReleaseRefused(failure.Error, failure.Message, (int)response.StatusCode) { File = failure.File }
            : new ReleaseRefused(Unreached, $"the service answered {(int)response.StatusCode}", 502);
    }
}
