using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// The configuration a device asks to hold.
/// </summary>
public sealed record HoldRequest(string? Key);

/// <summary>
/// Until when a device holds a configuration, in unix seconds.
/// </summary>
public sealed record HoldBody(long Until);

/// <summary>
/// Why a device is not let in and since when another one holds the configuration, in unix seconds.
/// </summary>
public sealed record BusyBody(string Error, string Message, long Since);

/// <summary>
/// Answers the clients that read a subscription and names where they read it.
/// </summary>
public static class SubscriptionAnswer
{
    /// <summary>
    /// The header a device names itself in.
    /// </summary>
    public const string DeviceHeader = "X-Hwid";

    private const string HoldTail = "/hold";

    private const int MaxDevice = 128;

    private const int PlainPort = 80;

    private const int SecurePort = 443;

    private const int RevisionLength = 32;

    /// <summary>
    /// Returns the subscription a path asks for, null for a path outside the subscriptions.
    /// </summary>
    public static string? Asked(PathString path, SubscriptionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var text = path.Value ?? string.Empty;
        var prefix = settings.Prefix;
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var id = text[prefix.Length..];

        return ClientRules.IsSubscription(id) ? id : null;
    }

    /// <summary>
    /// Returns the subscription a hold is asked of, null for a path that is not one.
    /// </summary>
    public static string? AskedHold(PathString path, SubscriptionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var text = path.Value ?? string.Empty;

        return text.EndsWith(HoldTail, StringComparison.Ordinal)
            ? Asked(new PathString(text[..^HoldTail.Length]), settings)
            : null;
    }

    /// <summary>
    /// Writes the subscription or the hold a request asks for, 404 when no client carries it.
    /// </summary>
    public static async Task WriteAsync(HttpContext context, SubscriptionSettings settings, IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scopes);

        if (AskedHold(context.Request.Path, settings) is { } holding)
        {
            await HoldAsync(context, settings, scopes, holding).ConfigureAwait(false);

            return;
        }

        var id = Asked(context.Request.Path, settings);
        if (id is null || !HttpMethods.IsGet(context.Request.Method) || !Named(context, settings.Domains))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        using var scope = scopes.CreateScope();
        var feed = await scope.ServiceProvider.GetRequiredService<SubscriptionFeed>()
            .ReadAsync(id, context.RequestAborted)
            .ConfigureAwait(false);
        if (feed is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        var headers = context.Response.Headers;
        headers.CacheControl = "no-store";
        headers.ETag = "\"" + Revision(feed.Body) + "\"";
        headers["Subscription-Userinfo"] = feed.Usage;
        headers["Profile-Update-Interval"] = settings.UpdateHours.ToString(CultureInfo.InvariantCulture);
        if (settings.Title.Length > 0)
        {
            headers["Profile-Title"] = ClientFeed.Title(settings.Title);
        }

        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(feed.Body, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the address a client of an endpoint reads its subscription at, empty when the subscriptions are off or
    /// the client carries none.
    /// </summary>
    public static string Address(
        SubscriptionSettings settings,
        PanelSettings panel,
        bool panelSecure,
        string host,
        ServerConfig endpoint,
        string id)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(id);

        if (!settings.IsEnabled || id.Length == 0)
        {
            return string.Empty;
        }

        if (!settings.Separate)
        {
            var named = Name(settings, panel, endpoint.Host.Length > 0 ? endpoint.Host : host);

            return "https://" + Bracketed(named) + Port(ConfigServices.Port(endpoint), SecurePort) + settings.Prefix + id;
        }

        var secure = settings.Port == panel.Port ? panelSecure : settings.Certificate.Length > 0 || panelSecure;

        return (secure ? "https://" : "http://")
            + Bracketed(Name(settings, panel, host))
            + Port(settings.Port, secure ? SecurePort : PlainPort)
            + settings.Prefix
            + id;
    }

    /// <summary>
    /// Returns the mark of what a subscription hands out.
    /// </summary>
    public static string Revision(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..RevisionLength];
    }

    private static async Task HoldAsync(HttpContext context, SubscriptionSettings settings, IServiceScopeFactory scopes, string id)
    {
        var taking = HttpMethods.IsPost(context.Request.Method);
        if ((!taking && !HttpMethods.IsDelete(context.Request.Method)) || !Named(context, settings.Domains))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        var device = context.Request.Headers[DeviceHeader].ToString().Trim();
        if (device.Length is 0 or > MaxDevice)
        {
            await RefuseAsync(context, "no-device", $"the request names no device in {DeviceHeader}").ConfigureAwait(false);

            return;
        }

        var key = await KeyAsync(context).ConfigureAwait(false);
        if (key is null)
        {
            await RefuseAsync(context, "bad-key", "the body names no public key of a configuration").ConfigureAwait(false);

            return;
        }

        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var members = await services.GetRequiredService<ClientStore>()
            .SubscribedAsync(id, context.RequestAborted)
            .ConfigureAwait(false);
        if (!members.Any(member => string.Equals(member.PublicKey, key, StringComparison.Ordinal)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        var holds = services.GetRequiredService<DeviceHolds>();
        if (!taking)
        {
            holds.Drop(key, device);
            context.Response.StatusCode = StatusCodes.Status204NoContent;

            return;
        }

        var now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        var answer = holds.Take(key, device, now);
        if (answer.IsHeld)
        {
            await context.Response.WriteAsJsonAsync(new HoldBody(answer.Until.ToUnixTimeSeconds()), context.RequestAborted)
                .ConfigureAwait(false);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response
            .WriteAsJsonAsync(new BusyBody("config-busy", Busy(context), answer.Since.ToUnixTimeSeconds()), context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static async Task<string?> KeyAsync(HttpContext context)
    {
        try
        {
            var body = await context.Request.ReadFromJsonAsync<HoldRequest>(context.RequestAborted).ConfigureAwait(false);
            var key = body?.Key?.Trim();

            return Curve25519.IsKey(key) ? key : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Task RefuseAsync(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        return context.Response.WriteAsJsonAsync(new Failure(code, message), context.RequestAborted);
    }

    private static string Busy(HttpContext context) =>
        context.Request.Headers.AcceptLanguage.ToString().StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? "Этот конфиг уже подключён на другом устройстве"
            : "This configuration is already connected on another device";

    private static string Name(SubscriptionSettings settings, PanelSettings panel, string host)
    {
        if (settings.Domains.Count > 0)
        {
            return settings.Domains[0];
        }

        return panel.Domains.Count > 0 ? panel.Domains[0] : host;
    }

    private static string Port(int port, int usual) =>
        port == usual ? string.Empty : ":" + port.ToString(CultureInfo.InvariantCulture);

    private static string Bracketed(string host) =>
        !host.StartsWith('[') && IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? "[" + host + "]"
            : host;

    private static bool Named(HttpContext context, IReadOnlyList<string> domains)
    {
        if (domains.Count == 0)
        {
            return true;
        }

        foreach (var domain in domains)
        {
            if (string.Equals(context.Request.Host.Host, domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var caller = context.Connection.RemoteIpAddress;

        return caller is not null && IPAddress.IsLoopback(caller);
    }
}
