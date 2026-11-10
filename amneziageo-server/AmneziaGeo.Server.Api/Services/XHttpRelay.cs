using System.Buffers;
using System.Net;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Relays GET requests to an XHTTP listener.
/// </summary>
public static class XHttpRelay
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(5),
    }) { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade",
    };

    /// <summary>
    /// Checks the request envelope.
    /// </summary>
    public static bool Accepts(HttpContext context, string method = "GET")
    {
        if (context.Request.Method != method || method is not ("GET" or "POST"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return false;
        }

        if ((method == "GET" && (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding")))
            || context.Request.ContentLength is > 4096 || (method == "POST" && context.Request.Headers.ContainsKey("X-WG-Data"))
            || context.Request.Headers.ContainsKey("Upgrade"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Streams a GET response through the front.
    /// </summary>
    public static async Task PassAsync(HttpContext context, int port, ILogger? logger = null, CancellationToken runtimeStopping = default, string method = "GET")
    {
        using var ending = runtimeStopping.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, runtimeStopping) : null;
        var ct = ending?.Token ?? context.RequestAborted;
        if (!Accepts(context, method))
        {
            return;
        }

        var excluded = new HashSet<string>(HopHeaders, StringComparer.OrdinalIgnoreCase);
        foreach (var name in context.Request.Headers.Connection.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            excluded.Add(name);
        }

        using var request = new HttpRequestMessage(new HttpMethod(method),
            $"http://127.0.0.1:{port}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}")
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        if (method == "POST")
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            var body = new byte[4097];
            var length = 0;
            try
            {
                while (length < body.Length)
                {
                    var read = await context.Request.Body.ReadAsync(body.AsMemory(length), deadline.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    length += read;
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                (logger ?? context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(XHttpRelay)))
                    .LogDebug(ex, "the HTTP proxy request body did not finish");
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (length > 4096)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            request.Content = new ByteArrayContent(body, 0, length);
        }

        foreach (var header in context.Request.Headers.Where(header => !excluded.Contains(header.Key)
            && !header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
            && !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        logger ??= context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(XHttpRelay));
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var headers = context.Request.Headers.Where(header => header.Key.StartsWith("X-Data-", StringComparison.OrdinalIgnoreCase)).ToArray();
            logger.LogDebug("the XHTTP front relays {Method} over {Protocol}, body {Body}, data headers {Headers}, data characters {Characters}",
                context.Request.Method, context.Request.Protocol, context.Request.ContentLength ?? 0, headers.Length,
                headers.Sum(header => header.Value.Sum(value => value?.Length ?? 0)));
        }

        try
        {
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            context.Response.StatusCode = (int)response.StatusCode;
            var responseExcluded = new HashSet<string>(HopHeaders, StringComparer.OrdinalIgnoreCase);
            foreach (var name in response.Headers.Connection)
            {
                responseExcluded.Add(name);
            }

            foreach (var header in response.Headers.Concat(response.Content.Headers).Where(header => !responseExcluded.Contains(header.Key)))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            await context.Response.StartAsync(ct).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var disposing = stream.ConfigureAwait(false);
            var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
            try
            {
                while (true)
                {
                    var count = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                    if (count == 0)
                    {
                        break;
                    }

                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (ct.IsCancellationRequested && (ex is OperationCanceledException or HttpRequestException or IOException))
        {
            if (runtimeStopping.IsCancellationRequested)
            {
                if (context.Response.HasStarted)
                {
                    context.Abort();
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            logger.LogWarning(ex, "the XHTTP listener did not answer on port {Port}", port);
            if (context.Response.HasStarted)
            {
                context.Abort();
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }
    }
}
