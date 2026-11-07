using System.Globalization;
using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Answers authenticated transport availability checks.
/// </summary>
public static class XHttpProbe
{
    /// <summary>
    /// Extracts the nonce of a proxy check.
    /// </summary>
    public static bool Nonce(PathString path, string prefix, out string nonce)
    {
        var head = "/" + prefix.Trim('/') + "/_amneziageo/probe/";
        var value = path.Value ?? "";
        nonce = value.StartsWith(head, StringComparison.Ordinal) ? value[head.Length..] : "";
        return nonce.Length == 32 && nonce.All(char.IsAsciiHexDigit);
    }

    /// <summary>
    /// Reports runtime and optional destination permission.
    /// </summary>
    public static async Task AnswerAsync(HttpContext context, string nonce, Func<Task<bool>> authorized, bool running,
        Func<ProxyTargets?>? targets = null, string transport = "xhttp", Func<bool>? stillRunning = null)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.Headers.Allow = "GET";
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding")
            || context.Request.Headers.ContainsKey("Upgrade"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!await authorized().ConfigureAwait(false))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var destination = default(ProxyDestination?);
        if (context.Request.Query.ContainsKey("host") || context.Request.Query.ContainsKey("port"))
        {
            var host = context.Request.Query["host"];
            var port = context.Request.Query["port"];
            if (host.Count != 1 || port.Count != 1 || !ProxyTargetRules.TryHost(host.ToString(), out var normalized)
                || !int.TryParse(port.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number is < 1 or > 65535)
            {
                await RefuseAsync(context, 400, "bad-proxy-target").ConfigureAwait(false);
                return;
            }

            if (!ProxyTargetRules.Allows(targets?.Invoke(), normalized, number))
            {
                await RefuseAsync(context, 403, "proxy-target-denied").ConfigureAwait(false);
                return;
            }

            destination = new ProxyDestination(normalized, number);
        }

        if (!running || stillRunning is not null && !stillRunning())
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (destination is null)
        {
            await context.Response.WriteAsJsonAsync(new { nonce, transport, running = true }, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            await context.Response.WriteAsJsonAsync(new { nonce, transport, running = true, target = destination, allowed = true },
                context.RequestAborted).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reports a proxy target refusal.
    /// </summary>
    public static async Task RefuseAsync(HttpContext context, int status, string error)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new { error }, context.RequestAborted).ConfigureAwait(false);
    }
}
