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
        var basic = await rules.ReadBasicAsync(ct).ConfigureAwait(false);
        var lists = await presets.ListAsync(ct).ConfigureAwait(false);
        var geo = await sources.ListAsync(ct).ConfigureAwait(false);

        return new RoutingDoc(
            RoutingFile.FormatTag,
            RoutingFile.CurrentVersion,
            [.. held.OrderBy(rule => rule.Position).Select(ToRule)],
            new BasicBlock([.. basic.Direct], [.. basic.Block]),
            [.. lists.OrderBy(one => one.Name, StringComparer.OrdinalIgnoreCase).Select(ToPreset)],
            [.. geo.OrderBy(one => one.Position).Select(ToSource)]);
    }

    /// <summary>
    /// Writes the routing the document carries as JSON.
    /// </summary>
    public static string Text(RoutingDoc doc) =>
        JsonSerializer.Serialize(doc, RoutingFile.Options);

    private static RuleBlock ToRule(RouteRule rule) => new(
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
        rule.Protocol);

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
