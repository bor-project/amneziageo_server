using System.Buffers;
using System.Globalization;
using System.Text.Json;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Dal;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace AmneziaGeo.Server.Api.Hello;

/// <summary>
/// Answers the point of the server and the legs of a measurement.
/// </summary>
public sealed class HelloDesk
{
    /// <summary>
    /// The path the point of the server sits at.
    /// </summary>
    public const string Path = "/api/hello";

    /// <summary>
    /// The path the legs of a measurement sit under.
    /// </summary>
    public const string SpeedPath = "/api/speed";

    /// <summary>
    /// The header the countersign of the server travels in.
    /// </summary>
    public const string ProofHeader = "Amneziageo-Proof";

    private const int Chunk = 64 * 1024;

    private readonly IServiceScopeFactory _scopes;

    private readonly SpeedTickets _tickets;

    private readonly IReadOnlyList<IHelloFeature> _features;

    private readonly JsonSerializerOptions _json;

    /// <summary>
    /// ctor
    /// </summary>
    public HelloDesk(
        IServiceScopeFactory scopes,
        SpeedTickets tickets,
        IEnumerable<IHelloFeature> features,
        IOptions<JsonOptions> json)
    {
        ArgumentNullException.ThrowIfNull(json);

        _scopes = scopes;
        _tickets = tickets;
        _features = [.. features];
        _json = json.Value.SerializerOptions;
    }

    /// <summary>
    /// Answers one request that reached the point.
    /// </summary>
    public async Task AnswerAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Request.Path;
        var method = context.Request.Method;
        if (path.Equals(Path, StringComparison.Ordinal) && HttpMethods.IsGet(method))
        {
            await WriteAsync(context, StatusCodes.Status200OK, new HelloResponse(FeatureNames.Server, Version(), _tickets.Challenge()))
                .ConfigureAwait(false);

            return;
        }

        if (path.Equals(Path, StringComparison.Ordinal) && HttpMethods.IsPost(method))
        {
            await ProveAsync(context).ConfigureAwait(false);

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

    private async Task ProveAsync(HttpContext context)
    {
        var ct = context.RequestAborted;
        var request = await ReadAsync(context).ConfigureAwait(false);
        if (request is null)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, "bad-request", "the body is not a hello request").ConfigureAwait(false);

            return;
        }

        using var scope = _scopes.CreateScope();
        var client = await scope.ServiceProvider.GetRequiredService<ClientStore>()
            .FindByKeyAsync(Trim(request.Key), ct)
            .ConfigureAwait(false);
        if (client is null || !client.IsEnabled)
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, "unknown-peer", "the panel carries no such peer").ConfigureAwait(false);

            return;
        }

        var endpoint = await scope.ServiceProvider.GetRequiredService<ConfigStore>()
            .FindAsync(client.ConfigId, ct)
            .ConfigureAwait(false);
        if (endpoint is null || !endpoint.IsEnabled)
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, "unknown-peer", "the endpoint of that peer is not up").ConfigureAwait(false);

            return;
        }

        var nonce = Trim(request.Nonce);
        if (!PeerProof.IsNonce(nonce))
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, "bad-nonce", "the nonce is not 32 bytes in base64").ConfigureAwait(false);

            return;
        }

        if (!_tickets.Takes(Trim(request.Challenge)))
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, "stale-challenge", "that challenge was not handed out or it has run out").ConfigureAwait(false);

            return;
        }

        if (!PeerProof.Holds(endpoint.PrivateKey, client.PublicKey, Trim(request.Challenge), Trim(request.Proof)))
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, "bad-proof", "the answer does not come from the key of that peer").ConfigureAwait(false);

            return;
        }

        var peer = new HelloPeer(client, endpoint, context);
        var offered = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var feature in _features)
        {
            if (await feature.OfferAsync(peer, ct).ConfigureAwait(false) is { } arguments)
            {
                offered[feature.Name] = arguments;
            }
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(
            new FeatureResponse(FeatureNames.Server, Version(), client.Name, offered),
            _json);
        context.Response.Headers[ProofHeader] = PeerProof.Countersign(endpoint.PrivateKey, client.PublicKey, nonce, body);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, ct).ConfigureAwait(false);
    }

    private async Task DownAsync(HttpContext context)
    {
        if (_tickets.Find(context.Request.Query["ticket"]) is not { } ticket)
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, "unknown-ticket", "that pass is not one of ours").ConfigureAwait(false);

            return;
        }

        if (!_tickets.Enter(ticket.Value))
        {
            await RefuseAsync(context, StatusCodes.Status429TooManyRequests, "measuring", "that pass is already measuring").ConfigureAwait(false);

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
            await RefuseAsync(context, StatusCodes.Status403Forbidden, "unknown-ticket", "that pass is not one of ours").ConfigureAwait(false);

            return;
        }

        if (!_tickets.Enter(ticket.Value))
        {
            await RefuseAsync(context, StatusCodes.Status429TooManyRequests, "measuring", "that pass is already measuring").ConfigureAwait(false);

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

    private Task RefuseAsync(HttpContext context, int status, string code, string message) =>
        WriteAsync(context, status, new Failure(code, message));

    private async Task WriteAsync<T>(HttpContext context, int status, T value)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(value, _json, context.RequestAborted).ConfigureAwait(false);
    }

    private static string Version() =>
        typeof(HelloDesk).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static string Trim(string? value) => value is null ? string.Empty : value.Trim();
}
