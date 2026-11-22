using System.Globalization;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Names proxy publications.
/// </summary>
public static class ProxyText
{
    /// <summary>
    /// Formats a proxy publication name.
    /// </summary>
    public static string Title(TransportProxyEntity proxy, string template, string host = "") =>
        ProxyName.Fill(template, Values(proxy, host));

    /// <summary>
    /// Reads proxy name substitutions.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Values(TransportProxyEntity proxy, string host = "")
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(host);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOST"] = proxy.Host.Length > 0 ? proxy.Host : host,
            ["PROXY"] = proxy.Name,
            ["TRANSPORT"] = proxy.IsHttp ? "http" : proxy.Transport,
            ["ID"] = proxy.Id.ToString(CultureInfo.InvariantCulture),
            ["PORT"] = proxy.Port.ToString(CultureInfo.InvariantCulture),
            ["PATH"] = proxy.Path.Trim('/'),
        };
    }
}
