using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Config;
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

    private readonly EndpointHost _host;

    private readonly ServiceServer _services;

    private readonly FirewallApplier _firewall;

    /// <summary>
    /// ctor
    /// </summary>
    public TemplateSpread(
        ConfigStore configs,
        ClientStore clients,
        EndpointHost host,
        ServiceServer services,
        FirewallApplier firewall)
    {
        _configs = configs;
        _clients = clients;
        _host = host;
        _services = services;
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
            await _services.SettleAsync(ct).ConfigureAwait(false);
            await _firewall.SettleAsync(ct).ConfigureAwait(false);
        }

        return done;
    }
}
