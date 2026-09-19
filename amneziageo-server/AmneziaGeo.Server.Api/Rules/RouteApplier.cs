using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Geo.Files;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Rules;

/// <summary>
/// Holds the plan the host was last given.
/// </summary>
public sealed class RoutePlans
{
    private RoutePlan? _held;

    /// <summary>
    /// Returns the plan built last, or null when none was built yet.
    /// </summary>
    public RoutePlan? Held => Volatile.Read(ref _held);

    /// <summary>
    /// Keeps a plan as the one built last.
    /// </summary>
    public void Keep(RoutePlan plan) => Volatile.Write(ref _held, plan);
}

/// <summary>
/// Builds the routing plan out of what the panel holds and puts it on the host.
/// </summary>
public sealed class RouteApplier
{
    private readonly RouteStore _rules;

    private readonly OutboundStore _outbounds;

    private readonly BalanceStore _balancers;

    private readonly ConfigStore _configs;

    private readonly GeoStore _geo;

    private readonly DnsStore _dns;

    private readonly DnsState _resolver;

    private readonly IGeoFileStore _files;

    private readonly RoutePlans _plans;

    private readonly BalanceLive _live;

    private readonly DnsSets _sets;

    private readonly ClientStore _clients;

    /// <summary>
    /// ctor
    /// </summary>
    public RouteApplier(
        RouteStore rules,
        OutboundStore outbounds,
        BalanceStore balancers,
        ConfigStore configs,
        GeoStore geo,
        DnsStore dns,
        DnsState resolver,
        IGeoFileStore files,
        RoutePlans plans,
        BalanceLive live,
        DnsSets sets,
        ClientStore clients)
    {
        _rules = rules;
        _outbounds = outbounds;
        _balancers = balancers;
        _configs = configs;
        _geo = geo;
        _dns = dns;
        _resolver = resolver;
        _files = files;
        _plans = plans;
        _live = live;
        _sets = sets;
        _clients = clients;
    }

    /// <summary>
    /// Expands the rules the panel holds over the geo databases and the outbounds.
    /// </summary>
    public async Task<RoutePlan> BuildAsync(CancellationToken ct)
    {
        var rules = await _rules.ListAsync(ct).ConfigureAwait(false);
        var outbounds = await _outbounds.ListAsync(ct).ConfigureAwait(false);
        var balancers = await _balancers.ListAsync(ct).ConfigureAwait(false);
        var configs = await _configs.ListAsync(ct).ConfigureAwait(false);
        var sources = await _geo.ListAsync(ct).ConfigureAwait(false);
        var resolver = _resolver.Settings ?? await _dns.ReadAsync(ct).ConfigureAwait(false);
        var clients = await _clients.ListAsync(ct).ConfigureAwait(false);
        var basic = await _rules.ReadBasicAsync(ct).ConfigureAwait(false);
        var plan = RoutePlan.Build(
            rules,
            outbounds,
            GeoIndex.Load(sources, _files),
            [.. configs.Select(config => config.Name)],
            resolver,
            balancers,
            _live.Alive,
            clients,
            basic);

        _plans.Keep(plan);

        return plan;
    }

    /// <summary>
    /// Returns the plan built last, building one when there is none.
    /// </summary>
    public async Task<RoutePlan> ReadAsync(CancellationToken ct) =>
        _plans.Held ?? await BuildAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// Puts the rules on the host.
    /// </summary>
    public async Task<RoutePlan> ApplyAsync(CancellationToken ct)
    {
        var plan = await BuildAsync(ct).ConfigureAwait(false);
        await _sets.LayAsync(RouteRuleset.Text(plan), plan, ct).ConfigureAwait(false);

        return plan;
    }

    /// <summary>
    /// Puts the rules on the host anew when one of them names clients.
    /// </summary>
    public async Task FollowClientsAsync(CancellationToken ct)
    {
        var plan = await ReadAsync(ct).ConfigureAwait(false);
        if (plan.Legs.Any(leg => leg.Rule.Clients.Count > 0))
        {
            await SettleAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Puts the rules on the host, leaving a host that refuses to the next command.
    /// </summary>
    public async Task<RoutePlan> SettleAsync(CancellationToken ct)
    {
        var plan = await BuildAsync(ct).ConfigureAwait(false);
        try
        {
            await _sets.LayAsync(RouteRuleset.Text(plan), plan, ct).ConfigureAwait(false);
        }
        catch (HostNetworkException)
        {
        }

        return plan;
    }
}
