using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Takes the host and the scheme a reverse proxy forwards.
/// </summary>
public static class Forwarded
{
    /// <summary>
    /// Reads the forwarded headers of the proxies the configuration names, and nothing when it names none.
    /// </summary>
    public static WebApplication UseForwarded(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<WebOptions>();
        var proxies = options.Proxies
            .Select(one => IPAddress.TryParse(one.Trim(), out var address) ? address : null)
            .OfType<IPAddress>()
            .ToArray();

        if (proxies.Length == 0)
        {
            return app;
        }

        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
        };

        foreach (var proxy in proxies)
        {
            forwarded.KnownProxies.Add(proxy);
        }

        app.UseForwardedHeaders(forwarded);

        return app;
    }
}
