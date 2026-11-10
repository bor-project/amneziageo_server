using System.Text.Json;

using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Route;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Dal.Exchange;

/// <summary>
/// Takes the routing of a server together for a file: the rules, the basic lists, the routing presets and the
/// addresses of the geo sources.
/// </summary>
public static class RoutingExport
{
    /// <summary>
    /// Reads the stores the panel holds and returns the routing of the server as a file carries it.
    /// </summary>
    public static async Task<RoutingDoc> TakeAsync(
        RouteStore rules,
        PresetStore presets,
        GeoStore sources,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(sources);

        var held = await rules.ListAsync(ct).ConfigureAwait(false);
        var table = await rules.TableAsync(ct).ConfigureAwait(false);
        var groups = await rules.GroupsAsync(ct).ConfigureAwait(false);
        var groupNames = groups.ToDictionary(group => group.Id, group => group.Name);
        var basic = await rules.ReadBasicAsync(ct).ConfigureAwait(false);
        var lists = await presets.ListAsync(ct).ConfigureAwait(false);
        var geo = await sources.ListAsync(ct).ConfigureAwait(false);
        var legacy = basic.Rules().Concat(held).Select(rule => rule.Id).SequenceEqual(table.Select(rule => rule.Id))
            && table.Where(rule => RouteBasic.Holds(rule.Id)).All(rule =>
                rule.IsEnabled && rule.Groups.Count == 0 && rule.Clients.Count == 0 && rule.Inbounds.Count == 0
                && rule.Ports.Count == 0 && rule.SourcePorts.Count == 0 && rule.Protocol == RouteProtocol.Any
                && rule.Action == (rule.Id == RouteBasic.BlockId ? RouteAction.Block : RouteAction.Direct)
                && rule.Name == (rule.Id == RouteBasic.BlockId ? RouteBasic.BlockName : RouteBasic.DirectName));

        return new RoutingDoc(
            RoutingFile.FormatTag,
            RoutingFile.CurrentVersion,
            [.. (legacy ? held : table).OrderBy(rule => rule.Position).Select(rule => ToRule(rule, groupNames))],
            legacy ? new BasicBlock([.. basic.Direct], [.. basic.Block]) : new BasicBlock([], []),
            [.. lists.OrderBy(one => one.Name, StringComparer.OrdinalIgnoreCase).Select(ToPreset)],
            [.. geo.OrderBy(one => one.Position).Select(ToSource)],
            [.. groups.Select(group => new AddressGroupBlock(group.Name, group.Targets, group.Dns))],
            !legacy);
    }

    /// <summary>
    /// Writes the routing the document carries as JSON.
    /// </summary>
    public static string Text(RoutingDoc doc) =>
        JsonSerializer.Serialize(doc, RoutingFile.Options);

    private static RuleBlock ToRule(RouteRule rule, IReadOnlyDictionary<long, string> groups) => new(
        rule.Name,
        rule.Position,
        rule.IsEnabled,
        rule.Action,
        rule.Outbound,
        rule.Targets,
        rule.Clients,
        rule.Inbounds,
        rule.Ports,
        rule.SourcePorts,
        rule.Protocol,
        [.. rule.Groups.Select(id => groups.TryGetValue(id, out var name) ? name : "#" + id)],
        RouteBasic.Holds(rule.Id) ? rule.Id : null);

    private static PresetBlock ToPreset(RoutingPreset one) => new(
        one.Name,
        one.Proxy,
        one.Direct,
        one.Block,
        one.AllUdp,
        one.Full,
        one.IsDefault,
        one.ReadOnly);

    private static SourceBlock ToSource(GeoSource one) => new(
        one.Name,
        one.Kind,
        one.Url,
        one.Position,
        one.IsEnabled);
}
