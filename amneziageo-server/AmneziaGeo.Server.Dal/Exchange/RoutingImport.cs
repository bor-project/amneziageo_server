using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Route;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Dal.Exchange;

/// <summary>
/// What an import of a routing file lays over the routing the server holds: what will be added, what will be
/// replaced, and the links the other server named that this one cannot carry out yet.
/// </summary>
/// <param name="Doc">The file the plan was taken from.</param>
/// <param name="NewRules">The rules the server holds none under the name of.</param>
/// <param name="ReplacedRules">The rules the server holds under the same name.</param>
/// <param name="ReplacesBasic">Whether the file carries basic lists to lay over the ones the server holds.</param>
/// <param name="NewPresets">The presets the server holds none under the name of.</param>
/// <param name="ReplacedPresets">The presets the server holds under the same name.</param>
/// <param name="NewSources">The geo sources the server holds none under the name of.</param>
/// <param name="ReplacedSources">The geo sources the server holds under the same name.</param>
/// <param name="MissingOutbounds">The channels the rules leave through that this server does not hold.</param>
/// <param name="MissingClients">The clients the rules name that this server does not hold.</param>
/// <param name="Detached">One line per rule that keeps at least one link this server cannot carry out.</param>
public sealed record RoutingPlan(
    RoutingDoc Doc,
    IReadOnlyList<string> NewRules,
    IReadOnlyList<string> ReplacedRules,
    bool ReplacesBasic,
    IReadOnlyList<string> NewPresets,
    IReadOnlyList<string> ReplacedPresets,
    IReadOnlyList<string> NewSources,
    IReadOnlyList<string> ReplacedSources,
    IReadOnlyList<string> MissingOutbounds,
    IReadOnlyList<string> MissingClients,
    IReadOnlyList<string> Detached)
{
    public IReadOnlyList<string> NewGroups { get; init; } = [];

    public IReadOnlyList<string> ReplacedGroups { get; init; } = [];
}

/// <summary>
/// What an import that was applied left behind.
/// </summary>
/// <param name="Added">One line per rule, preset and source the server took in.</param>
/// <param name="Replaced">One line per rule, preset and source the server laid the file over.</param>
/// <param name="BasicReplaced">Whether the basic lists were laid over.</param>
/// <param name="Refused">One line per rule, preset or source the server refused, with the reason.</param>
/// <param name="Detached">One line per rule that keeps a link this server does not hold yet.</param>
public sealed record RoutingAnswer(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Replaced,
    bool BasicReplaced,
    IReadOnlyList<string> Refused,
    IReadOnlyList<string> Detached);

/// <summary>
/// Takes a routing file in over the routing a server holds: a plan first, so the panel can show what will be added
/// and what will be replaced before anything is written, and the applying of one when the answer comes back.
/// </summary>
public static class RoutingImport
{
    /// <summary>
    /// Lays the plan over the stores: adds what the server holds none under the name of, replaces the ones it does,
    /// lays the basic lists over, and leaves everything else as it stood.
    /// </summary>
    public static async Task<RoutingAnswer> ApplyAsync(
        RoutingPlan plan,
        RouteStore rules,
        PresetStore presets,
        GeoStore sources,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(sources);

        var added = new List<string>();
        var replaced = new List<string>();
        var refused = new List<string>();
        var existingGroups = (await rules.GroupsAsync(ct).ConfigureAwait(false)).Select(group => group.Name).ToHashSet(StringComparer.Ordinal);
        var failedGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in plan.Doc.AddressGroups ?? [])
        {
            var result = await rules.ImportGroupAsync(new AddressGroup { Name = group.Name, Targets = group.Targets ?? [] }, ct).ConfigureAwait(false);
            Keep(result.Fault is null, existingGroups.Contains(group.Name) ? replaced : added, refused, $"address group '{group.Name}'", result.Fault?.Message ?? string.Empty);
            if (result.Fault is not null)
            {
                failedGroups.Add(group.Name);
            }
        }

        var groupIds = (await rules.GroupsAsync(ct).ConfigureAwait(false)).ToDictionary(group => group.Name, group => group.Id, StringComparer.Ordinal);
        var ordered = new List<long>();

        var held = await rules.ListAsync(ct).ConfigureAwait(false);
        var byName = held.GroupBy(rule => rule.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var one in plan.Doc.Rules.OrderBy(rule => rule.Position))
        {
            if ((one.Groups ?? []).Any(name => !groupIds.ContainsKey(name) || failedGroups.Contains(name)))
            {
                refused.Add($"rule '{one.Name}': an address group is missing");
                continue;
            }

            var draft = ToRule(one) with { Groups = [.. (one.Groups ?? []).Select(name => groupIds[name])] };
            if (one.LegacyId is { } legacy)
            {
                var result = await rules.ImportExceptionAsync(draft with { Id = legacy }, ct).ConfigureAwait(false);
                Keep(result.IsOk, replaced, refused, $"rule '{one.Name}'", result.Message);
                if (result.IsOk)
                {
                    ordered.Add(result.Record!.Id);
                }

                continue;
            }

            if (byName.TryGetValue(one.Name, out var found))
            {
                var result = await rules.ChangeAsync(found.Id, draft, ct).ConfigureAwait(false);
                Keep(result.IsOk, replaced, refused, $"rule '{one.Name}'", result.Message);
                if (result.IsOk)
                {
                    ordered.Add(result.Record!.Id);
                }
                continue;
            }

            var took = await rules.AddAsync(draft, ct).ConfigureAwait(false);
            Keep(took.IsOk, added, refused, $"rule '{one.Name}'", took.Message);
            if (took.IsOk)
            {
                ordered.Add(took.Record!.Id);
            }
        }

        if (plan.Doc.Unified)
        {
            foreach (var id in new[] { RouteBasic.BlockId, RouteBasic.DirectId }.Where(id => !plan.Doc.Rules.Any(rule => rule.LegacyId == id)))
            {
                await rules.RemoveAsync(id, ct).ConfigureAwait(false);
            }

            for (var at = 0; at < ordered.Count; at++)
            {
                await rules.PlaceAsync(ordered[at], at + 1, ct, table: true).ConfigureAwait(false);
            }
        }

        var basicReplaced = false;
        if (plan.ReplacesBasic && !plan.Doc.Unified)
        {
            var fault = await rules.SaveBasicAsync(
                new RouteBasic { Direct = plan.Doc.Basic.Direct, Block = plan.Doc.Basic.Block },
                ct).ConfigureAwait(false);
            if (fault is null)
            {
                basicReplaced = true;
            }
            else
            {
                refused.Add($"basic lists: {fault.Message}");
            }
        }
        else if (plan.Doc.Unified)
        {
            basicReplaced = true;
        }

        var heldPresets = await presets.ListAsync(ct).ConfigureAwait(false);
        var presetByName = heldPresets.GroupBy(one => one.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var one in plan.Doc.Presets.OrderBy(one => one.Name, StringComparer.OrdinalIgnoreCase))
        {
            var draft = ToPreset(one);
            if (presetByName.TryGetValue(one.Name, out var found))
            {
                var result = await presets.ChangeAsync(found.Id, draft, ct).ConfigureAwait(false);
                Keep(result.IsOk, replaced, refused, $"preset '{one.Name}'", result.Message);
                continue;
            }

            var took = await presets.AddAsync(draft, ct).ConfigureAwait(false);
            Keep(took.IsOk, added, refused, $"preset '{one.Name}'", took.Message);
        }

        var heldSources = await sources.ListAsync(ct).ConfigureAwait(false);
        var sourceByName = heldSources.GroupBy(one => one.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var one in plan.Doc.Sources.OrderBy(one => one.Position))
        {
            var draft = ToSource(one);
            if (sourceByName.TryGetValue(one.Name, out var found))
            {
                var result = await sources.ChangeAsync(found.Id, draft, ct).ConfigureAwait(false);
                Keep(result.IsOk, replaced, refused, $"source '{one.Name}'", result.Message);
                continue;
            }

            var took = await sources.AddAsync(draft, ct).ConfigureAwait(false);
            Keep(took.IsOk, added, refused, $"source '{one.Name}'", took.Message);
        }

        return new RoutingAnswer(added, replaced, basicReplaced, refused, plan.Detached);
    }

    /// <summary>
    /// Takes the routing the file carries against the stores the server holds and answers what will be added, what
    /// will be replaced, and which rules stay off the host until the channels and clients they name grow on this
    /// server. Nothing is written.
    /// </summary>
    public static async Task<RoutingPlan> PlanAsync(
        RoutingDoc doc,
        RouteStore rules,
        PresetStore presets,
        GeoStore sources,
        OutboundStore outbounds,
        ClientStore clients,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(outbounds);
        ArgumentNullException.ThrowIfNull(clients);

        var held = await rules.TableAsync(ct).ConfigureAwait(false);
        var heldNames = held.Select(rule => rule.Name).ToHashSet(StringComparer.Ordinal);
        var heldGroups = (await rules.GroupsAsync(ct).ConfigureAwait(false)).Select(group => group.Name).ToHashSet(StringComparer.Ordinal);
        var heldPresets = (await presets.ListAsync(ct).ConfigureAwait(false))
            .Select(one => one.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var heldSources = (await sources.ListAsync(ct).ConfigureAwait(false))
            .Select(one => one.Name).ToHashSet(StringComparer.Ordinal);

        var newRules = doc.Rules.Where(rule => !heldNames.Contains(rule.Name)).Select(rule => rule.Name).ToList();
        var replacedRules = doc.Rules.Where(rule => heldNames.Contains(rule.Name)).Select(rule => rule.Name).ToList();
        var newPresets = doc.Presets.Where(one => !heldPresets.Contains(one.Name)).Select(one => one.Name).ToList();
        var replacedPresets = doc.Presets.Where(one => heldPresets.Contains(one.Name)).Select(one => one.Name).ToList();
        var newSources = doc.Sources.Where(one => !heldSources.Contains(one.Name)).Select(one => one.Name).ToList();
        var replacedSources = doc.Sources.Where(one => heldSources.Contains(one.Name)).Select(one => one.Name).ToList();

        var ways = (await outbounds.ListAsync(ct).ConfigureAwait(false))
            .Select(one => one.Name).ToHashSet(StringComparer.Ordinal);
        var known = (await clients.ListAsync(ct).ConfigureAwait(false))
            .Select(one => one.Name).ToHashSet(StringComparer.Ordinal);

        var missingOutbounds = new SortedSet<string>(StringComparer.Ordinal);
        var missingClients = new SortedSet<string>(StringComparer.Ordinal);
        var detached = new List<string>();
        foreach (var rule in doc.Rules.OrderBy(rule => rule.Position))
        {
            var outbound = rule.Outbound.Trim();
            var goneOutbound = outbound.Length > 0 && !ways.Contains(outbound);
            var goneClients = rule.Clients.Where(client => !known.Contains(client)).ToList();
            if (goneOutbound)
            {
                missingOutbounds.Add(outbound);
            }

            foreach (var client in goneClients)
            {
                missingClients.Add(client);
            }

            if (!goneOutbound && goneClients.Count == 0)
            {
                continue;
            }

            detached.Add($"rule '{rule.Name}': {Why(goneOutbound, outbound, goneClients)}");
        }

        return new RoutingPlan(
            doc,
            newRules,
            replacedRules,
            doc.Unified || doc.Basic.Direct.Count > 0 || doc.Basic.Block.Count > 0,
            newPresets,
            replacedPresets,
            newSources,
            replacedSources,
            [.. missingOutbounds],
            [.. missingClients],
            detached)
        {
            NewGroups = [.. (doc.AddressGroups ?? []).Where(group => !heldGroups.Contains(group.Name)).Select(group => group.Name)],
            ReplacedGroups = [.. (doc.AddressGroups ?? []).Where(group => heldGroups.Contains(group.Name)).Select(group => group.Name)],
        };
    }

    private static void Keep(bool took, List<string> landed, List<string> refused, string line, string message)
    {
        if (took)
        {
            landed.Add(line);
            return;
        }

        refused.Add($"{line}: {message}");
    }

    private static string Why(bool goneOutbound, string outbound, IReadOnlyCollection<string> goneClients)
    {
        if (goneOutbound && goneClients.Count > 0)
        {
            return $"the outbound '{outbound}' and the clients {Named(goneClients)} are not on this server";
        }

        return goneOutbound
            ? $"the outbound '{outbound}' is not on this server"
            : $"the clients {Named(goneClients)} are not on this server";
    }

    private static string Named(IReadOnlyCollection<string> names) =>
        string.Join(", ", names.Select(name => $"'{name}'"));

    private static RouteRule ToRule(RuleBlock one) => new()
    {
        Name = one.Name,
        Position = one.Position,
        IsEnabled = one.IsEnabled,
        Action = one.Action,
        Outbound = one.Outbound,
        Targets = one.Targets,
        Clients = one.Clients,
        Inbounds = one.Inbounds,
        Ports = one.Ports,
        SourcePorts = one.SourcePorts,
        Protocol = one.Protocol,
    };

    private static RoutingPreset ToPreset(PresetBlock one) => new()
    {
        Name = one.Name,
        Proxy = one.Proxy,
        Direct = one.Direct,
        Block = one.Block,
        AllUdp = one.AllUdp,
        Full = one.Full,
        IsDefault = one.IsDefault,
        ReadOnly = one.ReadOnly,
    };

    private static GeoSource ToSource(SourceBlock one) => new()
    {
        Name = one.Name,
        Kind = one.Kind,
        Url = one.Url,
        Position = one.Position,
        IsEnabled = one.IsEnabled,
    };
}
