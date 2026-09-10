using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// Answers the clients that read a subscription and names where they read it.
/// </summary>
public static class SubscriptionAnswer
{
    private const int PlainPort = 80;

    private const int SecurePort = 443;

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
    /// Writes the subscription a request asks for, 404 when no client carries it.
    /// </summary>
    public static async Task WriteAsync(HttpContext context, SubscriptionSettings settings, IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scopes);

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
    /// Returns the address a client reads its subscription at, empty when the subscriptions are off or the client
    /// carries none.
    /// </summary>
    public static string Address(SubscriptionSettings settings, PanelSettings panel, bool panelSecure, string host, string id)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(id);

        if (!settings.IsEnabled || id.Length == 0)
        {
            return string.Empty;
        }

        var secure = settings.Port == panel.Port ? panelSecure : settings.Certificate.Length > 0 || panelSecure;
        var port = settings.Port == (secure ? SecurePort : PlainPort)
            ? string.Empty
            : ":" + settings.Port.ToString(CultureInfo.InvariantCulture);

        return (secure ? "https://" : "http://") + Bracketed(Name(settings, panel, host)) + port + settings.Prefix + id;
    }

    private static string Name(SubscriptionSettings settings, PanelSettings panel, string host)
    {
        if (settings.Domains.Count > 0)
        {
            return settings.Domains[0];
        }

        return panel.Domains.Count > 0 ? panel.Domains[0] : host;
    }

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
