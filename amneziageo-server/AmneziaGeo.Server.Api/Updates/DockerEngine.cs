using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// Talks to the Docker daemon through its socket.
/// </summary>
public sealed class DockerEngine : IDisposable
{
    private static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;

    /// <summary>
    /// ctor
    /// </summary>
    public DockerEngine(string socket)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await connection.ConnectAsync(new UnixDomainSocketEndPoint(socket), ct).ConfigureAwait(false);
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }

                return new NetworkStream(connection, ownsSocket: true);
            },
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://docker/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Returns what the daemon knows of a container.
    /// </summary>
    public Task<JsonNode> ContainerAsync(string id, CancellationToken ct) =>
        CallAsync(HttpMethod.Get, $"containers/{id}/json", null, ct);

    /// <summary>
    /// Pulls an image pinned to its digest and waits until the daemon holds all of it.
    /// </summary>
    public async Task PullAsync(string pinned, TimeSpan limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pinned);

        var at = pinned.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            throw new ArgumentException($"'{pinned}' is not pinned to a digest", nameof(pinned));
        }

        var address = $"images/create?fromImage={Uri.EscapeDataString(pinned[..at])}&tag={Uri.EscapeDataString(pinned[(at + 1)..])}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        using var request = new HttpRequestMessage(HttpMethod.Post, address);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        await EnsureAsync(response, timeout.Token).ConfigureAwait(false);

        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
        {
            if (Fault(line) is { Length: > 0 } fault)
            {
                throw new InvalidOperationException($"docker did not pull {pinned}: {fault}");
            }
        }
    }

    /// <summary>
    /// Gives an image one more name.
    /// </summary>
    public Task TagAsync(string image, string repository, string tag, CancellationToken ct) =>
        CallAsync(
            HttpMethod.Post,
            $"images/{image}/tag?repo={Uri.EscapeDataString(repository)}&tag={Uri.EscapeDataString(tag)}",
            null,
            ct);

    /// <summary>
    /// Makes a container and returns its id.
    /// </summary>
    public async Task<string> CreateAsync(string name, JsonObject spec, CancellationToken ct)
    {
        var answer = await CallAsync(HttpMethod.Post, $"containers/create?name={Uri.EscapeDataString(name)}", spec, ct)
            .ConfigureAwait(false);
        var id = Text(answer["Id"]);

        return id.Length > 0 ? id : throw new InvalidOperationException("docker named no container it made");
    }

    /// <summary>
    /// Starts a container.
    /// </summary>
    public Task StartAsync(string id, CancellationToken ct) =>
        CallAsync(HttpMethod.Post, $"containers/{id}/start", null, ct);

    /// <summary>
    /// Returns the containers that carry a label, with their state.
    /// </summary>
    public async Task<IReadOnlyList<(string Id, string State)>> ListAsync(string label, CancellationToken ct)
    {
        var filters = Uri.EscapeDataString(new JsonObject { ["label"] = new JsonArray(label) }.ToJsonString());
        var answer = await CallAsync(HttpMethod.Get, $"containers/json?all=true&filters={filters}", null, ct)
            .ConfigureAwait(false);
        var found = new List<(string Id, string State)>();
        if (answer is not JsonArray list)
        {
            return found;
        }

        foreach (var one in list)
        {
            found.Add((Text(one?["Id"]), Text(one?["State"])));
        }

        return found;
    }

    /// <summary>
    /// Removes a container that does not run.
    /// </summary>
    public Task RemoveAsync(string id, CancellationToken ct) =>
        CallAsync(HttpMethod.Delete, $"containers/{id}", null, ct);

    /// <summary>
    /// Returns the text a JSON node holds, empty when it holds none.
    /// </summary>
    public static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private async Task<JsonNode> CallAsync(HttpMethod method, string address, JsonNode? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CallLimit);
        using var request = new HttpRequestMessage(method, address);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        await EnsureAsync(response, timeout.Token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        return text.Length == 0 ? new JsonObject() : JsonNode.Parse(text) ?? new JsonObject();
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
        {
            return;
        }

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        throw new InvalidOperationException($"docker answered {(int)response.StatusCode}: {Said(text, "message")}");
    }

    private static string Fault(string line) =>
        line.Contains("\"error\"", StringComparison.Ordinal) ? Said(line, "error") : string.Empty;

    private static string Said(string text, string field)
    {
        try
        {
            return Text(JsonNode.Parse(text)?[field]) is { Length: > 0 } said ? said : text.Trim();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return text.Trim();
        }
    }
}
