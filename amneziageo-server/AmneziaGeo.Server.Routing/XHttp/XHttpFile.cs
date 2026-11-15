using System.Text.Json;
using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Routing.XHttp;

/// <summary>
/// Defines an HTTP GET listener and its users.
/// </summary>
public sealed record XHttpProxy(long Id, int Port, string Path, IReadOnlyList<string> Credentials, ProxyTargets? Targets = null, string Method = "GET",
    bool AllowAnonymous = false, IReadOnlyList<string>? BearerIds = null);

/// <summary>
/// Renders the configuration of an HTTP GET listener.
/// </summary>
public static class XHttpFile
{
    /// <summary>
    /// Identifies the GET wire protocol.
    /// </summary>
    public const string Version = "2";

    /// <summary>
    /// Renders an HTTP GET listener.
    /// </summary>
    public static string Render(XHttpProxy proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(proxy.Id);
        if (proxy.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(proxy), "The listener port must be between 1 and 65535.");
        }

        if (proxy.Method is not ("GET" or "POST"))
        {
            throw new ArgumentException("The HTTP method must be GET or POST.", nameof(proxy));
        }

        ArgumentNullException.ThrowIfNull(proxy.Path);
        var path = proxy.Path.Trim('/');
        if (path.Length == 0 || path is "." or ".." || path.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not '~'))
        {
            throw new ArgumentException("The path must contain one nonempty URL segment.", nameof(proxy));
        }

        ArgumentNullException.ThrowIfNull(proxy.Credentials);
        if (proxy.Credentials.Count == 0 && !proxy.AllowAnonymous)
        {
            throw new ArgumentException("The listener requires at least one user.", nameof(proxy));
        }

        var users = ProxyAccess.Credentials(proxy.Credentials, proxy.AllowAnonymous).Select(ParseCredential)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var identities = users.Select(ProxyAccess.HttpIdentity).ToHashSet(StringComparer.Ordinal);
        var bearerIds = (proxy.BearerIds ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (bearerIds.Any(id => id is null || id.Length != 64 || !identities.Contains(id)))
        {
            throw new ArgumentException("The bearer identity does not belong to a listener credential.", nameof(proxy));
        }
        var targets = ProxyTargetRules.TryNormalize(proxy.Targets, out var normalized) && normalized is not { Local: true } ? normalized : ProxyTargetRules.Denied;
        var configuration = new
        {
            httpGet = new
            {
                version = 2,
                method = proxy.Method,
                port = proxy.Port,
                path = $"/{path}/",
                credentials = users,
                allowAnonymous = proxy.AllowAnonymous,
                bearerIds,
                restricted = targets?.Restricted ?? false,
                destinations = targets is { Restricted: true }
                    ? targets.Destinations.Select(destination => new { host = destination.Host, port = destination.Port }).ToArray() : [],
            },
        };
        return JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ParseCredential(string credential)
    {
        if (!Guid.TryParse(credential, out var id) || id == Guid.Empty)
        {
            throw new ArgumentException("Each listener user must have a nonempty GUID.", nameof(credential));
        }

        return id.ToString("D");
    }
}
