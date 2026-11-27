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
    IReadOnlyList<string> NewPresets,
    IReadOnlyList<string> ReplacedPresets,
    IReadOnlyList<string> NewSources,
    IReadOnlyList<string> ReplacedSources,
    IReadOnlyList<string> MissingOutbounds,
    IReadOnlyList<string> MissingClients,
    IReadOnlyList<string> Detached)
{
    public IReadOnlyList<string> NewEntries { get; init; } = [];

    public IReadOnlyList<string> ReplacedEntries { get; init; } = [];
}

/// <summary>
/// What an import that was applied left behind.
/// </summary>
/// <param name="Added">One line per rule, preset and source the server took in.</param>
/// <param name="Replaced">One line per rule, preset and source the server laid the file over.</param>
/// <param name="Refused">One line per rule, preset or source the server refused, with the reason.</param>
/// <param name="Detached">One line per rule that keeps a link this server does not hold yet.</param>
public sealed record RoutingAnswer(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Replaced,
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
        var heldCriteria = (await rules.ListCriteriaAsync(ct).ConfigureAwait(false)).ToDictionary(one => one.Name, StringComparer.Ordinal);
        var defaults = CriteriaForDefault(plan.Doc);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var one in plan.Doc.Rules.Where(one => one.Name != defaults))
        {
            var result = await rules.ImportCriterionAsync(ToCriterion(one), ct).ConfigureAwait(false);
            Keep(result.IsOk, heldCriteria.ContainsKey(one.Name) ? replaced : added, refused, $"traffic rule '{one.Name}'", result.Message);
            if (!result.IsOk)
            {
                failed.Add(one.Name);
            }
        }

        var known = (await rules.ListCriteriaAsync(ct).ConfigureAwait(false)).ToDictionary(one => one.Name, one => one.Id, StringComparer.Ordinal);
        var held = await rules.TableAsync(ct).ConfigureAwait(false);
        var byName = held.ToDictionary(one => one.Name, StringComparer.Ordinal);
        var fallback = held.SingleOrDefault(one => one.IsDefault);
        var ordered = new List<long>();
        foreach (var one in plan.Doc.Entries.OrderBy(one => one.Position))
        {
            if (one.IsDefault)
            {
                var imported = plan.Doc.Rules.SingleOrDefault(rule => rule.Name == one.Rules.SingleOrDefault());
                if (fallback is null || imported is null || failed.Contains(imported.Name))
                {
                    refused.Add($"routing entry '{one.Name}': the default policy cannot be applied");
                    continue;
                }

                var change = await rules.ChangeAsync(fallback.Id,
                    fallback with { Action = one.Action, Outbound = one.Outbound }, ct, ToCriterion(imported)).ConfigureAwait(false);
                Keep(change.IsOk, replaced, refused, $"routing entry '{fallback.Name}'", change.Message);
                continue;
            }

            if (one.Rules is not { Count: > 0 } || one.Rules.Any(name => !known.ContainsKey(name) || failed.Contains(name)))
            {
                refused.Add($"routing entry '{one.Name}': a traffic rule is missing");
                continue;
            }

            var draft = new RouteRule
            {
                Name = one.Name,
                IsEnabled = one.IsEnabled,
                Action = one.Action,
                Outbound = one.Outbound,
                RuleIds = [.. one.Rules.Select(name => known[name])],
                Clients = one.Clients ?? [],
                Inbounds = one.Inbounds ?? [],
                Ports = one.Ports ?? [],
                SourcePorts = one.SourcePorts ?? [],
                Protocol = one.Protocol,
            };
            var existing = byName.GetValueOrDefault(one.Name);
            var result = one.LegacyId is { } legacy && RouteBasic.Holds(legacy)
                ? await rules.ImportExceptionAsync(draft with { Id = legacy }, ct).ConfigureAwait(false)
                : existing is not null ? await rules.ChangeAsync(existing.Id, draft, ct).ConfigureAwait(false)
                : await rules.AddAsync(draft, ct).ConfigureAwait(false);
            Keep(result.IsOk, existing is not null ? replaced : added, refused, $"routing entry '{one.Name}'", result.Message);
            if (result.IsOk)
            {
                ordered.Add(result.Record!.Id);
            }
        }

        for (var at = 0; at < ordered.Count; at++)
        {
            await rules.PlaceAsync(ordered[at], at + 1, ct, table: true).ConfigureAwait(false);
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

        return new RoutingAnswer(added, replaced, refused, plan.Detached);
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
        CancellationToken ct,
        BalanceStore? balancers = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(outbounds);
        ArgumentNullException.ThrowIfNull(clients);

        var held = await rules.TableAsync(ct).ConfigureAwait(false);
        var fallback = held.SingleOrDefault(one => one.IsDefault);
        var definitions = await rules.ListCriteriaAsync(ct).ConfigureAwait(false);
        doc = Prepare(doc, held, fallback, definitions);
        var heldNames = definitions.Select(one => one.Name).ToHashSet(StringComparer.Ordinal);
        var entryNames = held.Select(one => one.Name).ToHashSet(StringComparer.Ordinal);
        var heldPresets = (await presets.ListAsync(ct).ConfigureAwait(false))
            .Select(one => one.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var heldSources = (await sources.ListAsync(ct).ConfigureAwait(false))
            .Select(one => one.Name).ToHashSet(StringComparer.Ordinal);

        var defaults = CriteriaForDefault(doc);
        var newRules = doc.Rules.Where(rule => rule.Name != defaults && !heldNames.Contains(rule.Name)).Select(rule => rule.Name).ToList();
        var replacedRules = doc.Rules.Where(rule => rule.Name != defaults && heldNames.Contains(rule.Name)).Select(rule => rule.Name).ToList();
        var newPresets = doc.Presets.Where(one => !heldPresets.Contains(one.Name)).Select(one => one.Name).ToList();
        var replacedPresets = doc.Presets.Where(one => heldPresets.Contains(one.Name)).Select(one => one.Name).ToList();
        var newSources = doc.Sources.Where(one => !heldSources.Contains(one.Name)).Select(one => one.Name).ToList();
        var replacedSources = doc.Sources.Where(one => heldSources.Contains(one.Name)).Select(one => one.Name).ToList();

        var ways = (await outbounds.ListAsync(ct).ConfigureAwait(false))
            .Select(one => one.Name).ToHashSet(StringComparer.Ordinal);
        if (balancers is not null)
        {
            ways.UnionWith((await balancers.ListAsync(ct).ConfigureAwait(false)).Select(one => one.Name));
        }
        var known = (await clients.ListAsync(ct).ConfigureAwait(false))
            .Select(one => one.Name).ToHashSet(StringComparer.Ordinal);

        var missingOutbounds = new SortedSet<string>(StringComparer.Ordinal);
        var missingClients = new SortedSet<string>(StringComparer.Ordinal);
        var detached = new List<string>();
        foreach (var rule in doc.Entries.OrderBy(rule => rule.Position))
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
            newPresets,
            replacedPresets,
            newSources,
            replacedSources,
            [.. missingOutbounds],
            [.. missingClients],
            detached)
        {
            NewEntries = [.. doc.Entries.Where(one => !one.IsDefault && !entryNames.Contains(one.Name)).Select(one => one.Name)],
            ReplacedEntries = [.. doc.Entries.Where(one => one.IsDefault ? fallback is not null : entryNames.Contains(one.Name))
                .Select(one => one.IsDefault ? fallback!.Name : one.Name)],
        };
    }

    private static RoutingDoc Prepare(
        RoutingDoc doc, IReadOnlyList<RouteRule> held, RouteRule? fallback, IReadOnlyList<TrafficRule> definitions)
    {
        var sourceRules = doc.Rules;
        var fallbackName = fallback?.Criteria.SingleOrDefault()?.Name;
        var exclusive = CriteriaForDefault(doc);
        var collided = sourceRules.FirstOrDefault(one => one.Name != exclusive
            && string.Equals(one.Name, fallbackName, StringComparison.OrdinalIgnoreCase));
        var safeCriterion = collided is null ? null : FreeName(collided.Name, sourceRules
            .Select(one => one.Name).ToHashSet(StringComparer.OrdinalIgnoreCase), name =>
                definitions.Any(one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase)
                    && !SameCriterion(one, collided)));

        if (safeCriterion is not null)
        {
            sourceRules = [.. sourceRules.Select(one => one == collided ? one with { Name = safeCriterion } : one)];
        }

        var selected = sourceRules.ToDictionary(one => one.Name, StringComparer.Ordinal);
        var taken = doc.Entries.Select(one => one.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fallback is not null)
        {
            taken.Add(fallback.Name);
        }
        var existing = held.Where(one => !one.IsDefault).ToDictionary(one => one.Name, StringComparer.Ordinal);
        var entries = new List<EntryBlock>();
        foreach (var one in doc.Entries.OrderBy(one => one.Position))
        {
            var references = safeCriterion is null ? one.Rules
                : one.Rules.Select(name => name == collided!.Name ? safeCriterion : name).ToArray();
            var name = !one.IsDefault && string.Equals(one.Name, fallback?.Name, StringComparison.OrdinalIgnoreCase)
                ? FreeName(one.Name, taken, candidate => existing.TryGetValue(candidate, out var prior)
                    && !SameEntry(prior, one, references))
                : one.Name;

            var first = one with { Name = name, Rules = references };
            if (one.IsDefault || references.Count < 2 || references.Distinct(StringComparer.Ordinal).Count() != references.Count
                || references.Any(key => !selected.TryGetValue(key, out var rule) || rule.Profile is not null
                    || RouteRules.Check(ToCriterion(rule)) is not null))
            {
                entries.Add(first);
                continue;
            }

            entries.Add(first with { Rules = [references[0]] });
            for (var part = 1; part < references.Count; part++)
            {
                var child = ChildName(first, part, references[part], existing, taken);
                entries.Add(first with { Name = child, Rules = [references[part]], LegacyId = null });
            }
        }

        return doc with { Rules = sourceRules, Entries = [.. entries.Select((one, at) => one with { Position = at + 1 })] };
    }

    private static string ChildName(EntryBlock row, int part, string criterion, IReadOnlyDictionary<string, RouteRule> existing, HashSet<string> taken)
    {
        var name = row.Name;
        if (existing.TryGetValue(name, out var root))
        {
            var marker = $" ({root.Id}:{part + 1}";
            var migrated = existing.Values.FirstOrDefault(row => !taken.Contains(row.Name) && row.Criteria.Count == 1
                && row.Criteria[0].Name == criterion && (row.Name.Contains(marker + ":", StringComparison.Ordinal)
                    || row.Name.EndsWith(marker + ")", StringComparison.Ordinal)));
            if (migrated is not null)
            {
                taken.Add(migrated.Name);
                return migrated.Name;
            }
        }

        var tail = $"-{part + 1}";
        return FreeName(name[..Math.Min(name.Length, RouteRules.MaxNameLength - tail.Length)] + tail, taken,
            candidate => existing.Values.Any(one => string.Equals(one.Name, candidate, StringComparison.OrdinalIgnoreCase)
                && !SameEntry(one, row, [criterion])));
    }

    private static bool SameEntry(RouteRule held, EntryBlock source, IReadOnlyList<string> criteria) =>
        held.Action == source.Action && held.Outbound == source.Outbound
        && held.Clients.SequenceEqual(source.Clients) && held.Inbounds.SequenceEqual(source.Inbounds)
        && held.Ports.SequenceEqual(source.Ports) && held.SourcePorts.SequenceEqual(source.SourcePorts)
        && held.Protocol == source.Protocol && held.Criteria.Select(one => one.Name).SequenceEqual(criteria);

    private static bool SameCriterion(TrafficRule held, TrafficRuleBlock source) =>
        held.DefaultKey == source.Profile && held.DnsMode == source.DnsMode
        && held.Targets.SequenceEqual(source.Targets) && held.Dns.SequenceEqual(source.Dns)
        && held.DnsMembers.Count == (source.DnsMembers?.Count ?? 0)
        && held.DnsMembers.All(pair => source.DnsMembers?.TryGetValue(pair.Key, out var dns) == true && pair.Value.SequenceEqual(dns));

    private static string FreeName(string name, HashSet<string> taken, Func<string, bool> blocked)
    {
        for (var at = 1; ; at++)
        {
            var tail = at == 1 ? string.Empty : $"-{at}";
            var candidate = name[..Math.Min(name.Length, RouteRules.MaxNameLength - tail.Length)] + tail;
            if (!blocked(candidate) && taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string? CriteriaForDefault(RoutingDoc doc)
    {
        var criterion = doc.Entries.SingleOrDefault(one => one.IsDefault)?.Rules.SingleOrDefault();
        return criterion is not null && !doc.Entries.Any(one => !one.IsDefault && one.Rules.Contains(criterion, StringComparer.Ordinal))
            ? criterion : null;
    }

    private static TrafficRule ToCriterion(TrafficRuleBlock source) => new()
    {
        Name = source.Name,
        DefaultKey = source.Profile,
        Targets = source.Targets ?? [],
        Dns = source.Dns ?? [],
        DnsMode = source.DnsMode,
        DnsMembers = source.DnsMembers ?? new Dictionary<string, IReadOnlyList<string>>(),
    };

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
