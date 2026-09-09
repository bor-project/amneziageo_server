using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Proxy;

namespace AmneziaGeo.Server.Api.Proxy;

/// <summary>
/// The certificate a proxy answers under.
/// </summary>
/// <param name="Chain">The path to the certificate chain.</param>
/// <param name="Key">The path to the key of the chain.</param>
public sealed record ProxyCertificate(string Chain, string Key);

/// <summary>
/// Puts the proxies the panel holds on the host.
/// </summary>
public sealed class ProxyApplier
{
    private readonly ProxyStore _store;

    private readonly ConfigStore _configs;

    private readonly PanelStore _panel;

    private readonly ProxyHost _host;

    private readonly WebOptions _options;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyApplier(ProxyStore store, ConfigStore configs, PanelStore panel, ProxyHost host, WebOptions options)
    {
        _store = store;
        _configs = configs;
        _panel = panel;
        _host = host;
        _options = options;
    }

    /// <summary>
    /// Puts every proxy that is turned on back on the host.
    /// </summary>
    public async Task SettleAsync(CancellationToken ct)
    {
        var held = await _store.ListAsync(ct).ConfigureAwait(false);
        foreach (var proxy in held)
        {
            if (proxy.IsEnabled)
            {
                await ApplyAsync(proxy, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Puts a proxy on the host, letting through the ports of the interfaces that are turned on.
    /// </summary>
    public async Task<ProxyState> ApplyAsync(ProxyConfig proxy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        var certificate = await CertificateAsync(proxy, ct).ConfigureAwait(false);
        var ports = await PortsAsync(ct).ConfigureAwait(false);

        return await _host.ApplyAsync(proxy, ports, certificate.Chain, certificate.Key, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns whether the service of a proxy is up.
    /// </summary>
    public async Task<ProxyState> StateAsync(ProxyConfig proxy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        return proxy.IsEnabled
            ? await _host.StateAsync(proxy.Name, ct).ConfigureAwait(false)
            : ProxyState.Down;
    }

    /// <summary>
    /// Takes a proxy off the host.
    /// </summary>
    public async Task<ProxyState> WithdrawAsync(string name, CancellationToken ct) =>
        await _host.WithdrawAsync(name, ct).ConfigureAwait(false);

    /// <summary>
    /// Returns the certificate a proxy takes: its own, then the one of the panel.
    /// </summary>
    public async Task<ProxyCertificate> CertificateAsync(ProxyConfig proxy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        return proxy.Certificate.Length > 0 && proxy.CertificateKey.Length > 0
            ? new ProxyCertificate(proxy.Certificate, proxy.CertificateKey)
            : await PanelCertificateAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the certificate a proxy answers under when it names none of its own.
    /// </summary>
    public async Task<ProxyCertificate> PanelCertificateAsync(CancellationToken ct)
    {
        var panel = await _panel.ReadAsync(ct).ConfigureAwait(false);

        return panel.Certificate.Length > 0 && panel.CertificateKey.Length > 0
            ? new ProxyCertificate(panel.Certificate, panel.CertificateKey)
            : new ProxyCertificate(_options.Certificate, _options.CertificateKey);
    }

    private async Task<int[]> PortsAsync(CancellationToken ct)
    {
        var held = await _configs.ListAsync(ct).ConfigureAwait(false);

        return [.. held.Where(config => config.IsEnabled).Select(config => config.ListenPort)];
    }
}
