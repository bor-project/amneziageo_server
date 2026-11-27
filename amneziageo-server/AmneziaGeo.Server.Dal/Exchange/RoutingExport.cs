using System.Text.Json;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Dal.Exchange;

/// <summary>
/// Exports traffic rules and routing entries.
/// </summary>
public static class RoutingExport
{
    public static async Task<RoutingDoc> TakeAsync(RouteStore rules, PresetStore presets, GeoStore sources, CancellationToken ct)
    {
        var criteria = await rules.ListCriteriaAsync(ct).ConfigureAwait(false);
        var names = criteria.ToDictionary(one => one.Id, one => one.Name);
        var entries = await rules.TableAsync(ct).ConfigureAwait(false);
        var lists = await presets.ListAsync(ct).ConfigureAwait(false);
        var geo = await sources.ListAsync(ct).ConfigureAwait(false);
        return new RoutingDoc(RoutingFile.FormatTag, RoutingFile.CurrentVersion,
            [.. criteria.Select(one => new TrafficRuleBlock(one.Name, one.Targets, one.Dns, one.DnsMembers, one.DnsMode,
                one.DefaultKey == RouteDefaults.InternetOnly ? one.DefaultKey : null))],
            [.. entries.Select(one => new EntryBlock(one.Name, one.Position, one.IsEnabled, one.Action, one.Outbound,
                [.. one.RuleIds.Select(id => names.TryGetValue(id, out var name) ? name : "#" + id)],
                one.Clients, one.Inbounds, one.Ports, one.SourcePorts, one.Protocol, RouteBasic.Holds(one.Id) ? one.Id : null, one.IsDefault))],
            [.. lists.Select(one => new PresetBlock(one.Name, one.Proxy, one.Direct, one.Block, one.AllUdp, one.Full, one.IsDefault, one.ReadOnly))],
            [.. geo.Select(one => new SourceBlock(one.Name, one.Kind, one.Url, one.Position, one.IsEnabled))]);
    }

    public static string Text(RoutingDoc doc) => JsonSerializer.Serialize(doc, RoutingFile.Options);
}
