using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Rules;

/// <summary>
/// Tells where the rules the host carries send the traffic asked about.
/// </summary>
public sealed class RouteTester
{
    private readonly RouteApplier _applier;

    private readonly RouteStore _rules;

    private readonly ClientStore _clients;

    private readonly ConfigStore _configs;

    private readonly OutboundStore _outbounds;

    private readonly BalanceStore _balancers;

    private readonly BalanceLive _live;

    private readonly DnsSets _sets;

    private readonly DnsHost _dns;

    /// <summary>
    /// ctor
    /// </summary>
    public RouteTester(
        RouteApplier applier,
        RouteStore rules,
        ClientStore clients,
        ConfigStore configs,
        OutboundStore outbounds,
        BalanceStore balancers,
        BalanceLive live,
        DnsSets sets,
        DnsHost dns)
    {
        _applier = applier;
        _rules = rules;
        _clients = clients;
        _configs = configs;
        _outbounds = outbounds;
        _balancers = balancers;
        _live = live;
        _sets = sets;
        _dns = dns;
    }

    /// <summary>
    /// Returns where the rules send the traffic, or why the question is unusable.
    /// </summary>
    public async Task<(RouteFault? Fault, RouteTestResponse? Answer)> TestAsync(
        RouteTestRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = Host(request.Target);
        var name = IPAddress.TryParse(target, out var bare) ? string.Empty : target;
        if (name.Length > 0 && RouteRules.Target(name) is not { Kind: GeoRuleKind.Domain })
        {
            return (new RouteFault("bad-test-target", $"'{target}' is not a name or an address"), null);
        }

        var protocol = (request.Protocol ?? RouteProtocol.Tcp).Trim().ToLowerInvariant();
        if (protocol is not (RouteProtocol.Tcp or RouteProtocol.Udp))
        {
            return (new RouteFault("bad-protocol", $"'{protocol}' is not a protocol the traffic carries"), null);
        }

        if (Port(request.Port) is null || Port(request.SourcePort) is null)
        {
            return (new RouteFault("bad-port", "a port runs from 1 to 65535"), null);
        }

        var (fault, sources, inbound) = await FromAsync(request.Client, ct).ConfigureAwait(false);
        if (fault is not null)
        {
            return (fault, null);
        }

        var addresses = bare is null ? await AskAsync(name, ct).ConfigureAwait(false) : new[] { bare };
        var plan = await _applier.ReadAsync(ct).ConfigureAwait(false);
        var query = new RouteQuery
        {
            Name = name.ToLowerInvariant(),
            Addresses = addresses,
            Port = request.Port ?? 0,
            SourcePort = request.SourcePort ?? 0,
            Protocol = protocol,
            Sources = sources,
            Inbound = inbound,
        };

        var verdict = RouteProbe.Test(plan, query, _sets.Holds);
        var rule = await RuleAsync(verdict.Leg, ct).ConfigureAwait(false);
        var exit = verdict.Leg is { } leg && leg.Rule.Action == RouteAction.Out
            ? await ExitAsync(leg, ct).ConfigureAwait(false)
            : null;

        return (null, new RouteTestResponse(
            verdict.Verdict,
            verdict.Guard,
            query.Name,
            [.. addresses.Select(address => address.ToString())],
            inbound,
            [.. sources.Select(address => address.ToString())],
            rule,
            exit,
            verdict.Steps));
    }

    private static string Host(string? text)
    {
        var body = (text ?? string.Empty).Trim();

        return body.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(body, UriKind.Absolute, out var uri)
            ? uri.IdnHost.Trim('[', ']')
            : body.TrimEnd('.');
    }

    private static int? Port(int? port) => port is null or (>= 1 and <= 65535) ? port ?? 0 : null;

    private async Task<(RouteFault? Fault, IReadOnlyList<IPAddress> Sources, string Inbound)> FromAsync(
        string? client,
        CancellationToken ct)
    {
        var body = (client ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            return (null, [], string.Empty);
        }

        var configs = await _configs.ListAsync(ct).ConfigureAwait(false);
        if (IPAddress.TryParse(body, out var address))
        {
            var holder = configs.FirstOrDefault(config => RouteProbe.Within(config.Address, address));

            return (null, [address], holder?.Name ?? string.Empty);
        }

        var clients = await _clients.ListAsync(ct).ConfigureAwait(false);
        var found = clients.FirstOrDefault(one => string.Equals(one.Name, body, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            return (new RouteFault("unknown-client", $"there is no client called '{body}'"), [], string.Empty);
        }

        var sources = found.Address
            .Select(text => AwgAllowedIp.TryParse(text, out var range) ? range.Address : null)
            .OfType<IPAddress>()
            .ToArray();

        return (null, sources, configs.FirstOrDefault(config => config.Id == found.ConfigId)?.Name ?? string.Empty);
    }

    private async Task<IReadOnlyList<IPAddress>> AskAsync(string name, CancellationToken ct)
    {
        var answered = await _dns.AskAsync(name, ct).ConfigureAwait(false);
        if (answered is not null)
        {
            return [.. answered.Distinct()];
        }

        try
        {
            return [.. (await System.Net.Dns.GetHostAddressesAsync(name, ct).ConfigureAwait(false)).Distinct()];
        }
        catch (SocketException)
        {
            return [];
        }
    }

    private async Task<RouteTestRule?> RuleAsync(RouteLeg? leg, CancellationToken ct)
    {
        if (leg is null)
        {
            return null;
        }

        var rules = await _rules.ListAsync(ct).ConfigureAwait(false);
        var place = rules.Select((rule, at) => (rule.Id, Place: at + 1)).FirstOrDefault(one => one.Id == leg.Rule.Id).Place;

        return new RouteTestRule(leg.Rule.Id, leg.Rule.Name, place, leg.Rule.Action, leg.Rule.Outbound);
    }

    private async Task<RouteTestExit> ExitAsync(RouteLeg leg, CancellationToken ct)
    {
        var outbounds = await _outbounds.ListAsync(ct).ConfigureAwait(false);
        var balancers = await _balancers.ListAsync(ct).ConfigureAwait(false);
        var name = leg.Rule.Outbound;
        var group = balancers.FirstOrDefault(one => string.Equals(one.Name, name, StringComparison.Ordinal));
        var names = group?.Members ?? [name];
        var members = names
            .Select(member => outbounds.FirstOrDefault(one => string.Equals(one.Name, member, StringComparison.Ordinal)))
            .OfType<OutboundConfig>()
            .Select(outbound => new RouteTestMember(
                outbound.Name,
                outbound.IsEnabled,
                _live.Alive?.Contains(outbound.Name) ?? true,
                leg.Exit.Marks.Contains(outbound.Mark)))
            .ToArray();

        return new RouteTestExit(name, group is not null, group?.Strategy ?? string.Empty, members);
    }
}
