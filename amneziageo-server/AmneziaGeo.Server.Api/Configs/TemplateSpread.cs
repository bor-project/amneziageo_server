using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Proxy;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// Carries the constants of a template into everything that takes it.
/// </summary>
public sealed class TemplateSpread
{
    private readonly ConfigStore _configs;

    private readonly ClientStore _clients;

    private readonly ProxyStore _proxies;

    private readonly EndpointHost _host;

    private readonly ProxyApplier _proxy;

    private readonly FirewallApplier _firewall;

    /// <summary>
    /// ctor
    /// </summary>
    public TemplateSpread(
        ConfigStore configs,
        ClientStore clients,
        ProxyStore proxies,
        EndpointHost host,
        ProxyApplier proxy,
        FirewallApplier firewall)
    {
        _configs = configs;
        _clients = clients;
        _proxies = proxies;
        _host = host;
        _proxy = proxy;
        _firewall = firewall;
    }

    /// <summary>
    /// Writes the constants of an endpoint template into every endpoint that takes it and raises them again.
    /// </summary>
    public async Task<IReadOnlyList<EndpointSync>> InterfacesAsync(InterfaceTemplate template, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);

        var held = await _configs.ListAsync(ct).ConfigureAwait(false);
        var done = new List<EndpointSync>();
        foreach (var config in held.Where(one => one.TemplateId == template.Id))
        {
            var result = await _configs.ChangeAsync(config.Id, template.Over(config), ct).ConfigureAwait(false);
            done.Add(result.IsOk
                ? await _host.ApplyAsync(result.Record!, ct).ConfigureAwait(false)
                : new EndpointSync(config.Name, false, result.Message));
        }

        if (done.Count > 0)
        {
            await _host.FirewallAsync(
                    await _configs.ListAsync(ct).ConfigureAwait(false),
                    await _clients.ListAsync(ct).ConfigureAwait(false),
                    ct)
                .ConfigureAwait(false);
            await _proxy.SettleAsync(ct).ConfigureAwait(false);
            await _firewall.SettleAsync(ct).ConfigureAwait(false);
        }

        return done;
    }

    /// <summary>
    /// Writes the constants of a proxy template into every proxy that takes it and starts them again.
    /// </summary>
    public async Task<IReadOnlyList<EndpointSync>> ProxiesAsync(ProxyTemplate template, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);

        var held = await _proxies.ListAsync(ct).ConfigureAwait(false);
        var done = new List<EndpointSync>();
        foreach (var proxy in held.Where(one => one.TemplateId == template.Id))
        {
            var result = await _proxies.ChangeAsync(proxy.Id, template.Over(proxy), ct).ConfigureAwait(false);
            if (!result.IsOk)
            {
                done.Add(new EndpointSync(proxy.Name, false, result.Message));
                continue;
            }

            if (!string.Equals(proxy.Kind, result.Record!.Kind, StringComparison.Ordinal))
            {
                await _proxy.WithdrawAsync(proxy.Name, proxy.Kind, ct).ConfigureAwait(false);
            }

            done.Add(await StartAsync(result.Record!, ct).ConfigureAwait(false));
        }

        if (done.Count > 0)
        {
            await _firewall.SettleAsync(ct).ConfigureAwait(false);
        }

        return done;
    }

    private async Task<EndpointSync> StartAsync(ProxyConfig proxy, CancellationToken ct)
    {
        if (!proxy.IsEnabled)
        {
            return new EndpointSync(proxy.Name, true, string.Empty);
        }

        var state = await _proxy.ApplyAsync(proxy, ct).ConfigureAwait(false);

        return new EndpointSync(proxy.Name, state.IsRunning, state.Message);
    }
}
