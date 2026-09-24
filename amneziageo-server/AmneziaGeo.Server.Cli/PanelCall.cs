using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Cli;

/// <summary>
/// Talks to the running panel under a token that lives for one command.
/// </summary>
public sealed class PanelCall : IAsyncDisposable
{
    /// <summary>
    /// The exit code when the panel does not answer.
    /// </summary>
    public const int Silent = 4;

    private const string HealthFile = "health";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Context _context;

    private readonly HttpClient _http;

    private readonly long _token;

    /// <summary>
    /// ctor
    /// </summary>
    private PanelCall(Context context, HttpClient http, long token)
    {
        _context = context;
        _http = http;
        _token = token;
    }

    /// <summary>
    /// Runs a piece of work against the panel under a token of its own and returns its exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Func<PanelCall, Task<int>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(work);

        var settings = await context.Panel.ReadAsync(ct).ConfigureAwait(false);
        var (root, own) = Where(context.Path, settings);
        var minted = await context.Tokens.MintAsync(Name(), Roles.Admin, 1, actor: null, address: null, ct).ConfigureAwait(false);
        if (!minted.IsOk || minted.Minted is null)
        {
            Terminal.Fail(minted.Message);

            return 1;
        }

        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, errors) => errors == SslPolicyErrors.None || own,
            },
        };
        var http = new HttpClient(handler) { BaseAddress = root, Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", minted.Minted.Secret);

        var call = new PanelCall(context, http, minted.Minted.Token.Id);
        await using var owned = call.ConfigureAwait(false);
        try
        {
            return await work(call).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            Terminal.Fail($"the panel does not answer at {root}: {ex.Message}");

            return Silent;
        }
        catch (TaskCanceledException)
        {
            Terminal.Fail($"the panel at {root} took too long to answer");

            return Silent;
        }
    }

    /// <summary>
    /// Sends a request to the panel and returns the status with the answer.
    /// </summary>
    public async Task<(int Status, JsonElement Body)> SendAsync(HttpMethod method, string route, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        return ((int)response.StatusCode, Parse(text));
    }

    /// <summary>
    /// Prints why the panel refused a request and returns the exit code.
    /// </summary>
    public static int Refused(int status, JsonElement body)
    {
        var said = Text(body, "message");
        Terminal.Fail(said.Length > 0 ? said : $"the panel answered {status.ToString(CultureInfo.InvariantCulture)}");

        return 1;
    }

    /// <summary>
    /// Returns a text property of an answer, empty when it has none.
    /// </summary>
    public static string Text(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Returns a number property of an answer, zero when it has none.
    /// </summary>
    public static long Number(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number
            : 0;

    /// <summary>
    /// Revokes the token and lets the connection go.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _context.Tokens.RevokeAsync(_token, actor: null, address: null, CancellationToken.None).ConfigureAwait(false);
    }

    // Returns where the panel answers the host: the address it wrote down at its start, under its path.
    private static (Uri Root, bool Own) Where(string database, PanelSettings settings)
    {
        var written = Written(Path.Combine(Path.GetDirectoryName(database) ?? ".", HealthFile));
        var told = Uri.TryCreate(written, UriKind.Absolute, out var parsed);
        var health = told && parsed is not null
            ? parsed
            : new Uri("http://127.0.0.1:" + settings.Port.ToString(CultureInfo.InvariantCulture) + "/");

        return (new Uri(health, settings.Prefix), told || health.IsLoopback);
    }

    // Returns the line a file holds, empty when there is none to read.
    private static string Written(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file).Trim() : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    // Returns the JSON an answer carries, nothing for an empty or broken one.
    private static JsonElement Parse(string text)
    {
        if (text.Length == 0)
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(text);

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    // Returns the name of a token for one command.
    private static string Name() =>
        "menu-" + TimeProvider.System.GetUtcNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
}
