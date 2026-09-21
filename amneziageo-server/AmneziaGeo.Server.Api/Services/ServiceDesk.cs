using System.Buffers;
using System.Globalization;
using System.Text.Json;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Answers the services of an endpoint: the hello, the legs of a measurement and the websocket of the tunnel.
/// </summary>
public sealed class ServiceDesk
{
    /// <summary>
    /// The path the hello sits at.
    /// </summary>
    public const string HelloPath = "/api/hello";

    /// <summary>
    /// The path the legs of a measurement sit under.
    /// </summary>
    public const string SpeedPath = "/api/speed";

    /// <summary>
    /// The path the websocket of the tunnel sits under.
    /// </summary>
    public const string FrontPath = "/v1";

    /// <summary>
    /// The scheme the token travels under in the header of a websocket.
    /// </summary>
    public const string TokenScheme = PeerToken.Scheme;

    private const int Chunk = 64 * 1024;

    private readonly IServiceScopeFactory _scopes;

    private readonly SpeedTickets _tickets;

    private readonly IReadOnlyList<IHelloFeature> _features;

    private readonly JsonSerializerOptions _json;

    private readonly ILogger<ServiceDesk> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public ServiceDesk(
        IServiceScopeFactory scopes,
        SpeedTickets tickets,
        IEnumerable<IHelloFeature> features,
        IOptions<JsonOptions> json,
        ILogger<ServiceDesk> logger)
    {
        ArgumentNullException.ThrowIfNull(json);

        _scopes = scopes;
        _tickets = tickets;
        _features = [.. features];
        _json = json.Value.SerializerOptions;
        _logger = logger;
    }

    /// <summary>
    /// Answers one request that reached the services of an endpoint.
    /// </summary>
    public async Task AnswerAsync(HttpContext context, ServicePoint point)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(point);

        var path = context.Request.Path;
        var method = context.Request.Method;
        if (point.WebSocket
            && path.StartsWithSegments(FrontPath, StringComparison.Ordinal)
            && context.Features.Get<IHttpUpgradeFeature>() is { IsUpgradableRequest: true })
        {
            await PassAsync(context, point).ConfigureAwait(false);

            return;
        }

        if (path.Equals(HelloPath, StringComparison.Ordinal) && HttpMethods.IsPost(method))
        {
            await HelloAsync(context, point).ConfigureAwait(false);

            return;
        }

        if (path.Equals(SpeedPath + "/down", StringComparison.Ordinal) && HttpMethods.IsGet(method))
        {
            await DownAsync(context).ConfigureAwait(false);

            return;
        }

        if (path.Equals(SpeedPath + "/up", StringComparison.Ordinal) && HttpMethods.IsPost(method))
        {
            await UpAsync(context).ConfigureAwait(false);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    /// <summary>
    /// Reads the token a websocket carries in its header, or null when it carries none.
    /// </summary>
    public static HelloRequest? Token(string? header, JsonSerializerOptions json)
    {
        var prefix = TokenScheme + " ";
        if (header is null || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var text = header[prefix.Length..].Trim().Replace('-', '+').Replace('_', '/');
            var padded = text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=');

            return JsonSerializer.Deserialize<HelloRequest>(Convert.FromBase64String(padded), json);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private async Task HelloAsync(HttpContext context, ServicePoint point)
    {
        var ct = context.RequestAborted;
        var request = await ReadAsync(context).ConfigureAwait(false);
        if (request is null)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, new HelloFailure("bad-request", "the body is not a hello request"))
                .ConfigureAwait(false);

            return;
        }

        var proven = await ProveAsync(request, point, true, ct).ConfigureAwait(false);
        if (proven.Failure is { } failure)
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, failure).ConfigureAwait(false);

            return;
        }

        var peer = new HelloPeer(proven.Client!, proven.Endpoint!, proven.Template, context);
        var offered = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var feature in _features)
        {
            if (feature.Offer(peer) is { } arguments)
            {
                offered[feature.Name] = arguments;
            }
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(
            new FeatureResponse(FeatureNames.Server, Version(), peer.Client.Name, offered),
            _json);
        context.Response.Headers.CacheControl = "no-store";
        await WriteAsync(context, StatusCodes.Status200OK, PeerToken.Seal(proven.Shared, Trim(request.Nonce), body))
            .ConfigureAwait(false);
    }

    private async Task PassAsync(HttpContext context, ServicePoint point)
    {
        var token = Token(context.Request.Headers.Authorization, _json);
        var proven = token is null
            ? Proven.No(new HelloFailure("no-token", "the websocket carries no token"))
            : await ProveAsync(token, point, false, context.RequestAborted).ConfigureAwait(false);
        if (proven.Failure is { } failure)
        {
            _logger.LogInformation("the websocket of {Endpoint} was refused: {Reason}", point.Name, failure.Error);
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        await FrontRelay.PassAsync(context, point.Front, _logger).ConfigureAwait(false);
    }

    // Proves a token; a hello takes its nonce once, a websocket may come again with it within the window.
    private async Task<Proven> ProveAsync(HelloRequest request, ServicePoint point, bool once, CancellationToken ct)
    {
        var key = Trim(request.Key);
        var nonce = Trim(request.Nonce);
        if (!Curve25519.IsKey(key))
        {
            return Proven.No(new HelloFailure("bad-key", "the key is not a key of 32 bytes in base64"));
        }

        if (!PeerToken.IsNonce(nonce))
        {
            return Proven.No(new HelloFailure("bad-nonce", "the nonce is not 16 bytes in base64"));
        }

        var now = _tickets.Now;
        if (!PeerToken.Fresh(request.Time, now))
        {
            return Proven.No(new HelloFailure("stale-time", "the token was counted too far from the clock of the server", now.ToUnixTimeSeconds()));
        }

        using var scope = _scopes.CreateScope();
        var client = await scope.ServiceProvider.GetRequiredService<ClientStore>().FindByKeyAsync(key, ct).ConfigureAwait(false);
        if (client is null || !client.IsEnabled || client.ConfigId != point.ConfigId)
        {
            return Proven.No(new HelloFailure("unknown-peer", "the endpoint carries no such peer"));
        }

        var endpoint = await scope.ServiceProvider.GetRequiredService<ConfigStore>().FindAsync(client.ConfigId, ct).ConfigureAwait(false);
        if (endpoint is null || !endpoint.IsEnabled)
        {
            return Proven.No(new HelloFailure("unknown-peer", "the endpoint of that peer is not up"));
        }

        var shared = PeerToken.Shared(endpoint.PrivateKey, client.PublicKey);
        if (!PeerToken.Holds(shared, key, request.Time, nonce, Trim(request.Proof)))
        {
            return Proven.No(new HelloFailure("bad-proof", "the token does not come from the key of that peer"));
        }

        if (once && !_tickets.Takes(nonce))
        {
            return Proven.No(new HelloFailure("replayed", "that token was already taken"));
        }

        var template = client.TemplateId is { } id
            ? await scope.ServiceProvider.GetRequiredService<TemplateStore>().FindAsync(id, ct).ConfigureAwait(false)
            : null;

        return new Proven(client, endpoint, template, shared, null);
    }

    private async Task DownAsync(HttpContext context)
    {
        if (_tickets.Find(context.Request.Query["ticket"]) is not { } ticket)
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, new HelloFailure("unknown-ticket", "that pass is not one of ours"))
                .ConfigureAwait(false);

            return;
        }

        if (!_tickets.Enter(ticket.Value))
        {
            await RefuseAsync(context, StatusCodes.Status429TooManyRequests, new HelloFailure("measuring", "that pass is already measuring"))
                .ConfigureAwait(false);

            return;
        }

        try
        {
            var asked = long.TryParse(context.Request.Query["bytes"], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                ? count
                : SpeedTickets.DefaultBytes;
            var size = Math.Clamp(asked, 1, SpeedTickets.MaxBytes);
            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength = size;
            context.Response.Headers.CacheControl = "no-store";

            var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
            try
            {
                Array.Clear(buffer);
                for (var left = size; left > 0 && !context.RequestAborted.IsCancellationRequested;)
                {
                    var step = (int)Math.Min(left, Chunk);
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, step), context.RequestAborted).ConfigureAwait(false);
                    left -= step;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }
        finally
        {
            _tickets.Leave(ticket.Value);
        }
    }

    private async Task UpAsync(HttpContext context)
    {
        if (_tickets.Find(context.Request.Query["ticket"]) is not { } ticket)
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, new HelloFailure("unknown-ticket", "that pass is not one of ours"))
                .ConfigureAwait(false);

            return;
        }

        if (!_tickets.Enter(ticket.Value))
        {
            await RefuseAsync(context, StatusCodes.Status429TooManyRequests, new HelloFailure("measuring", "that pass is already measuring"))
                .ConfigureAwait(false);

            return;
        }

        var taken = 0L;
        var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            while (taken < SpeedTickets.MaxBytes)
            {
                var step = await context.Request.Body
                    .ReadAsync(buffer.AsMemory(0, Chunk), context.RequestAborted)
                    .ConfigureAwait(false);
                if (step == 0)
                {
                    break;
                }

                taken += step;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            _tickets.Leave(ticket.Value);
        }

        context.Response.Headers.CacheControl = "no-store";
        await WriteAsync(context, StatusCodes.Status200OK, new { bytes = taken }).ConfigureAwait(false);
    }

    private async Task<HelloRequest?> ReadAsync(HttpContext context)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<HelloRequest>(context.Request.Body, _json, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task RefuseAsync(HttpContext context, int status, HelloFailure failure) =>
        WriteAsync(context, status, failure);

    private async Task WriteAsync<T>(HttpContext context, int status, T value)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(value, _json, context.RequestAborted).ConfigureAwait(false);
    }

    private static string Version() =>
        typeof(ServiceDesk).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static string Trim(string? value) => value is null ? string.Empty : value.Trim();

    // The client behind a token and what it shares with its endpoint, or why the token was refused.
    private sealed record Proven(
        TunnelClient? Client,
        ServerConfig? Endpoint,
        ClientTemplate? Template,
        byte[] Shared,
        HelloFailure? Failure)
    {
        public static Proven No(HelloFailure failure) => new(null, null, null, [], failure);
    }
}
