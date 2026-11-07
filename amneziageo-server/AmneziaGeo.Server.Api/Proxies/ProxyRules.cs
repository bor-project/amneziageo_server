using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Validates transport proxy settings and route ownership.
/// </summary>
public sealed class ProxyRules
{
    private readonly AppDbContext db;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyRules(AppDbContext context)
    {
        db = context;
    }
    /// <summary>
    /// Returns the conflicting entity for a path.
    /// </summary>
    public async Task<string?> ConflictAsync(int port, string path, long id, long? legacy, CancellationToken ct)
    {
        var wanted = path.Trim().Trim('/');
        var proxies = await db.TransportProxies.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var proxy = proxies.FirstOrDefault(one => one.Id != id && (one.ListenPort == 0 ? one.Port : one.ListenPort) == port && Same(one.Path, wanted));
        if (proxy is not null)
        {
            return "proxy " + proxy.Name;
        }

        var endpoints = await db.Configs.AsNoTracking().Where(one => one.WebSocket).ToListAsync(ct).ConfigureAwait(false);
        var endpoint = endpoints.FirstOrDefault(one => one.Id != legacy && (one.ServicesPort == 0 ? one.ListenPort : one.ServicesPort) == port
            && Same(one.WebSocketPath.Length > 0 ? one.WebSocketPath : ConfigServices.OldPath, wanted));
        if (endpoint is not null)
        {
            return "websocket " + endpoint.Name;
        }

        var panel = await db.Panel.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (panel is not null && panel.Port == port && Same(panel.Path, wanted))
        {
            return "panel";
        }

        var subscription = await db.Subscription.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (subscription is { IsEnabled: true } && (!subscription.Separate || subscription.Port == port) && Same(subscription.Path, wanted))
        {
            return "subscriptions";
        }

        var updates = await db.ClientUpdates.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return updates is { IsEnabled: true } && updates.Source == "own" && (updates.Port == 0 ? panel?.Port : updates.Port) == port
            && Same(updates.Path, wanted) ? "client updates" : null;
    }

    /// <summary>
    /// Returns the invalid proxy field.
    /// </summary>
    public static Failure? Check(TransportProxyEntity proxy)
    {
        if (proxy.Name.Trim().Length is 0 or > 64)
        {
            return new Failure("bad-proxy-name", "the proxy name must contain 1 to 64 characters");
        }

        if (proxy.Transport is not ("websocket" or "xhttp"))
        {
            return new Failure("bad-proxy-transport", "the proxy transport is unknown");
        }

        if (proxy.Host.Length > 253 || Uri.CheckHostName(proxy.Host.Trim().Trim('[', ']')) == UriHostNameType.Unknown)
        {
            return new Failure("bad-proxy-host", "the proxy needs a public host");
        }

        if (proxy.Port is < 1 or > 65535 || proxy.ListenPort is < 0 or > 65535)
        {
            return new Failure("bad-proxy-port", "the TCP port must be between 1 and 65535");
        }

        if (proxy.OriginHttp && proxy.Transport != "xhttp")
        {
            return new Failure("bad-proxy-transport", "HTTP origin requires XHTTP");
        }

        var path = proxy.Path.Trim().Trim('/');
        if (path.Length is 0 or > 64 || !path.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_')
            || path.Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            return new Failure("bad-proxy-path", "the proxy path must be one segment of letters, digits, dashes or underscores");
        }

        if (proxy.ServerName.Length > 0 && Uri.CheckHostName(proxy.ServerName) == UriHostNameType.Unknown)
        {
            return new Failure("bad-proxy-host", "the TLS server name is not a host");
        }

        if (!ProxyTargetRules.TryNormalize(ProxyTargetRules.Read(proxy.TargetsJson), out _))
        {
            return new Failure("bad-proxy-targets", "the proxy needs one to 64 valid UDP destinations");
        }

        return proxy.Pin.Length > 0 && (proxy.Pin.Length != 64 || !proxy.Pin.All(char.IsAsciiHexDigit))
            ? new Failure("bad-proxy-pin", "the certificate pin must be a SHA-256 digest") : null;
    }

    private static bool Same(string left, string right) => string.Equals(left.Trim('/'), right, StringComparison.OrdinalIgnoreCase);
}
