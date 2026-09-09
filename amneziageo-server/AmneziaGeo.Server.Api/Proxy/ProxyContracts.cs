using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Proxy;

namespace AmneziaGeo.Server.Api.Proxy;

/// <summary>
/// A proxy as the panel reads it.
/// </summary>
public sealed record ProxyResponse(
    long Id,
    string Name,
    bool IsEnabled,
    int Port,
    string Path,
    string Certificate,
    string CertificateKey,
    bool IsRunning,
    string Message);

/// <summary>
/// The settings a proxy is created or changed with.
/// </summary>
public sealed record ProxyRequest(
    string? Name,
    bool IsEnabled,
    int Port,
    string? Path,
    string? Certificate,
    string? CertificateKey);

/// <summary>
/// What turning a proxy on or off carries.
/// </summary>
public sealed record ProxySwitchRequest(bool On);

/// <summary>
/// Turns a proxy into what the panel reads and back.
/// </summary>
public static class ProxyAnswers
{
    /// <summary>
    /// Returns a proxy with what the host answers about its service.
    /// </summary>
    public static ProxyResponse Proxy(ProxyConfig proxy, ProxyState state)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(state);

        return new ProxyResponse(
            proxy.Id,
            proxy.Name,
            proxy.IsEnabled,
            proxy.Port,
            proxy.Path,
            proxy.Certificate,
            proxy.CertificateKey,
            state.IsRunning,
            state.Message);
    }

    /// <summary>
    /// Returns what a request asks a proxy to become.
    /// </summary>
    public static ProxyConfig Draft(ProxyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ProxyConfig
        {
            Name = Trim(request.Name),
            IsEnabled = request.IsEnabled,
            Port = request.Port,
            Path = Trim(request.Path).Trim('/'),
            Certificate = Trim(request.Certificate),
            CertificateKey = Trim(request.CertificateKey),
        };
    }

    private static string Trim(string? value) => value is null ? string.Empty : value.Trim();
}
