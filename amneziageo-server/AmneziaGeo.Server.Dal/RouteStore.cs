using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What managing a routing rule produced.
/// </summary>
public enum RouteOutcome
{
    Ok = 0,
    Invalid = 1,
    NameTaken = 2,
    Unknown = 3,
}

/// <summary>
/// The rule a command produced, and why it was refused.
/// </summary>
public sealed record RouteResult(RouteOutcome Outcome, string Code, string Message, RouteRule? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == RouteOutcome.Ok;

    /// <summary>
    /// Returns the rule a command produced.
    /// </summary>
    public static RouteResult Done(RouteRule rule) => new(RouteOutcome.Ok, string.Empty, string.Empty, rule);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static RouteResult No(RouteOutcome outcome, string code, string message) =>
        new(outcome, code, message, null);
}

/// <summary>
/// The list of a rule a client or an endpoint is named in.
/// </summary>
public enum RuleSide
{
    Clients = 0,
    Inbounds = 1,
}

/// <summary>
/// Reads the names a rule matches by on one side.
/// </summary>
public static class RuleSides
{
    /// <summary>
    /// Returns the names on the side of a rule.
    /// </summary>
    public static IReadOnlyList<string> Of(this RouteRule rule, RuleSide side)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return side == RuleSide.Clients ? rule.Clients : rule.Inbounds;
    }

    /// <summary>
    /// Returns how the names of a side are told apart: clients whatever the case, endpoints exactly.
    /// </summary>
    public static StringComparer Comparer(this RuleSide side) =>
        side == RuleSide.Clients ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Returns the rules that name the client or the endpoint, by number.
    /// </summary>
    public static IReadOnlyList<long> Naming(IEnumerable<RouteRule> rules, RuleSide side, string name) =>
        [.. rules.Where(rule => rule.Of(side).Contains(name, side.Comparer())).Select(rule => rule.Id)];
}

/// <summary>
/// Adds, changes and removes the routing rules the panel holds.
/// </summary>
public sealed class RouteStore
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public RouteStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every rule the panel holds, in the order they are read in.
    /// </summary>
    public async Task<IReadOnlyList<RouteRule>> ListAsync(CancellationToken ct)
    {
        var found = await _db.Rules
            .Include(rule => rule.Matches).ThenInclude(match => match.Rule)
            .AsNoTracking()
            .Where(rule => rule.Id > 0)
            .OrderBy(rule => rule.Position).ThenBy(rule => rule.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns the complete rule order.
    /// </summary>
    public async Task<IReadOnlyList<RouteRule>> TableAsync(CancellationToken ct)
    {
        var found = await _db.Rules.Include(rule => rule.Matches).ThenInclude(match => match.Rule).AsNoTracking().OrderBy(rule => rule.Position).ThenBy(rule => rule.Id).ToListAsync(ct).ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    public Task<IReadOnlyList<TrafficRule>> ListCriteriaAsync(CancellationToken ct) => new TrafficRuleStore(_db, _time).ListAsync(ct);

    public async Task<TrafficRuleResult> ImportCriterionAsync(TrafficRule draft, CancellationToken ct)
    {
        var store = new TrafficRuleStore(_db, _time);
        var held = await _db.TrafficRules.AsNoTracking().FirstOrDefaultAsync(rule => rule.Name == draft.Name, ct).ConfigureAwait(false);
        return held is null
            ? await store.AddAsync(draft, ct).ConfigureAwait(false)
            : await store.ChangeAsync(held.Id, draft, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports a legacy exception.
    /// </summary>
    public Task<RouteResult> ImportExceptionAsync(RouteRule draft, CancellationToken ct) =>
        _db.AloneAsync(async () =>
        {
            if (!RouteBasic.Holds(draft.Id))
            {
                return Missing(draft.Id);
            }

            var held = await _db.Rules.Include(rule => rule.Matches).ThenInclude(match => match.Rule).FirstOrDefaultAsync(rule => rule.Id == draft.Id, ct).ConfigureAwait(false);
            if (held is not null)
            {
                return await ChangeCoreAsync(draft.Id, draft, ct).ConfigureAwait(false);
            }

            var full = await WithCriteriaAsync(draft, ct).ConfigureAwait(false);
            if (Check(full) is { } fault)
            {
                return RouteResult.No(RouteOutcome.Invalid, fault.Code, fault.Message);
            }

            if (await UnknownRuleAsync(draft.RuleIds, ct).ConfigureAwait(false) is { } missing)
            {
                return missing;
            }

            if (await _db.Rules.AnyAsync(rule => rule.Name == draft.Name, ct).ConfigureAwait(false))
            {
                return Taken(draft.Name);
            }

            var now = _time.GetUtcNow();
            var entity = new RoutingEntryEntity { Id = draft.Id, CreatedUtc = now, UpdatedUtc = now };
            await WriteAsync(entity, full, ct).ConfigureAwait(false);
            _db.Rules.Add(entity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            return RouteResult.Done(Read(entity));
        }, ct);

    /// <summary>
    /// Returns one rule, or null when the panel holds none under the number.
    /// </summary>
    public async Task<RouteRule?> FindAsync(long id, CancellationToken ct)
    {
        var found = await _db.Rules
            .Include(rule => rule.Matches).ThenInclude(match => match.Rule)
            .AsNoTracking()
            .FirstOrDefaultAsync(rule => rule.Id == id, ct)
            .ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Adds a rule, giving it the place at the end.
    /// </summary>
    public Task<RouteResult> AddAsync(RouteRule draft, CancellationToken ct) =>
        _db.AloneAsync(() => AddCoreAsync(draft, ct), ct);

    public Task<RouteResult?> AddInternetOnlyAsync(IReadOnlyList<long> interfaceIds, string name, string addressesName, CancellationToken ct) =>
        _db.AloneAsync(async () =>
        {
            var ids = interfaceIds.Distinct().ToArray();
            if (ids.Length is 0 or > RouteRules.MaxConditions)
            {
                return RouteResult.No(RouteOutcome.Invalid, "profile-inbounds", "select between one and 64 VPN interfaces");
            }

            var inbounds = await _db.Configs.AsNoTracking().Where(config => ids.Contains(config.Id))
                .OrderBy(config => config.Name).Select(config => config.Name).ToListAsync(ct).ConfigureAwait(false);
            if (inbounds.Count != ids.Length)
            {
                return RouteResult.No(RouteOutcome.Invalid, "profile-inbounds", "one or more VPN interfaces no longer exist");
            }

            var rows = await TableAsync(ct).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            var addresses = await _db.TrafficRules.FirstOrDefaultAsync(
                rule => rule.DefaultKey == RouteDefaults.InternetOnly, ct).ConfigureAwait(false);
            if (addresses is null)
            {
                var names = await _db.TrafficRules.Select(rule => rule.Name).ToListAsync(ct).ConfigureAwait(false);
                addresses = new TrafficRuleEntity
                {
                    DefaultKey = RouteDefaults.InternetOnly,
                    Name = ProfileName(addressesName, names),
                    Targets = string.Join(", ", RouteDefaults.PrivateNetworks),
                    DnsMode = TrafficDnsMode.Default,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                };
            }

            var covered = rows.Where(row => row.Action == RouteAction.Block && row.Protocol == RouteProtocol.Any
                && row.Criteria.Count == 1 && row.Criteria[0].Id == addresses.Id && row.Inbounds.Count > 0
                && row.Clients.Count == 0 && row.Ports.Count == 0 && row.SourcePorts.Count == 0)
                .SelectMany(row => row.Inbounds).ToHashSet(StringComparer.Ordinal);
            var selected = inbounds.Where(inbound => !covered.Contains(inbound)).ToArray();
            if (selected.Length == 0)
            {
                return null;
            }

            var criterion = TrafficRuleStore.Read(addresses);
            var draft = RouteDefaults.Fresh(ProfileName(name, rows.Select(row => row.Name))) with
            {
                Action = RouteAction.Block,
                Inbounds = selected,
                Targets = criterion.Targets,
                Criteria = [criterion],
            };
            if (criterion.Targets.Count == 0)
            {
                return RouteResult.No(RouteOutcome.Invalid, "empty-addresses", "the private networks set is empty");
            }

            if (RouteRules.Check(draft) is { } fault)
            {
                return RouteResult.No(RouteOutcome.Invalid, fault.Code, fault.Message);
            }

            var entity = new RoutingEntryEntity
            {
                Name = draft.Name,
                Position = await NextPositionAsync(ct).ConfigureAwait(false),
                IsEnabled = draft.IsEnabled,
                Action = draft.Action,
                Protocol = draft.Protocol,
                Inbounds = string.Join(", ", draft.Inbounds),
                CreatedUtc = now,
                UpdatedUtc = now,
                Matches = [new RoutingEntryRuleEntity { Rule = addresses }],
            };
            _db.Rules.Add(entity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return RouteResult.Done(Read(entity));
        }, ct);

    private static string ProfileName(string name, IEnumerable<string> existing)
    {
        var names = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = name;
        for (var number = 2; names.Contains(candidate); number++)
        {
            var suffix = $" ({number.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            candidate = name[..Math.Min(name.Length, RouteRules.MaxNameLength - suffix.Length)] + suffix;
        }

        return candidate;
    }

    private async Task<RouteResult> AddCoreAsync(RouteRule draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await _db.Rules.AnyAsync(rule => rule.Name == draft.Name, ct).ConfigureAwait(false))
        {
            return Taken(draft.Name);
        }

        var full = await WithCriteriaAsync(draft with { Id = 0, Position = await NextPositionAsync(ct).ConfigureAwait(false) }, ct).ConfigureAwait(false);
        if (Check(full) is { } fault)
        {
            return RouteResult.No(RouteOutcome.Invalid, fault.Code, fault.Message);
        }

        if (await UnknownRuleAsync(full.RuleIds, ct).ConfigureAwait(false) is { } missing)
        {
            return missing;
        }

        var now = _time.GetUtcNow();
        var entity = new RoutingEntryEntity { CreatedUtc = now, UpdatedUtc = now };
        await WriteAsync(entity, full, ct).ConfigureAwait(false);

        _db.Rules.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return RouteResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the settings of a rule, keeping its place.
    /// </summary>
    public Task<RouteResult> ChangeAsync(long id, RouteRule draft, CancellationToken ct) =>
        _db.AloneAsync(() => ChangeCoreAsync(id, draft, ct), ct);

    private async Task<RouteResult> ChangeCoreAsync(long id, RouteRule draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var entity = await _db.Rules.Include(rule => rule.Matches).ThenInclude(match => match.Rule).FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        if (await _db.Rules.AnyAsync(rule => rule.Name == draft.Name && rule.Id != id, ct).ConfigureAwait(false))
        {
            return Taken(draft.Name);
        }

        var full = await WithCriteriaAsync(draft with { Id = id, Position = entity.Position }, ct).ConfigureAwait(false);
        if (Check(full) is { } fault)
        {
            return RouteResult.No(RouteOutcome.Invalid, fault.Code, fault.Message);
        }

        if (await UnknownRuleAsync(full.RuleIds, ct).ConfigureAwait(false) is { } missing)
        {
            return missing;
        }

        await WriteAsync(entity, full, ct).ConfigureAwait(false);
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return RouteResult.Done(Read(entity));
    }

    /// <summary>
    /// Turns a rule on or off.
    /// </summary>
    public async Task<RouteResult> SwitchAsync(long id, bool on, CancellationToken ct)
    {
        var entity = await _db.Rules.Include(rule => rule.Matches).ThenInclude(match => match.Rule).FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        entity.IsEnabled = on;
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return RouteResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes a rule.
    /// </summary>
    public async Task<RouteResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.Rules.Include(rule => rule.Matches).ThenInclude(match => match.Rule).FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var gone = Read(entity);
        _db.Rules.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return RouteResult.Done(gone);
    }

    /// <summary>
    /// Swaps a rule with the one above or below it.
    /// </summary>
    public async Task<RouteResult> MoveAsync(long id, bool up, CancellationToken ct, bool table = false)
    {
        var entity = await _db.Rules.Include(rule => rule.Matches).ThenInclude(match => match.Rule).FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var neighbour = up
            ? await _db.Rules.Where(one => (table || one.Id > 0) && one.Position < entity.Position)
                .OrderByDescending(one => one.Position)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
            : await _db.Rules.Where(one => (table || one.Id > 0) && one.Position > entity.Position)
                .OrderBy(one => one.Position)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

        if (neighbour is null)
        {
            return RouteResult.Done(Read(entity));
        }

        (entity.Position, neighbour.Position) = (neighbour.Position, entity.Position);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return RouteResult.Done(Read(entity));
    }

    /// <summary>
    /// Puts a rule at a place in the order, counting from one.
    /// </summary>
    public async Task<RouteResult> PlaceAsync(long id, int place, CancellationToken ct, bool table = false)
    {
        var all = await _db.Rules.Include(rule => rule.Matches).ThenInclude(match => match.Rule).OrderBy(rule => rule.Position).ThenBy(rule => rule.Id).ToListAsync(ct).ConfigureAwait(false);
        var movable = all.Where(rule => table || rule.Id > 0).ToList();
        var entity = movable.FirstOrDefault(rule => rule.Id == id);
        if (entity is null)
        {
            return Missing(id);
        }

        var positions = movable.Select(rule => rule.Position).ToArray();
        movable.Remove(entity);
        movable.Insert(Math.Clamp(place, 1, movable.Count + 1) - 1, entity);
        for (var at = 0; at < movable.Count; at++)
        {
            movable[at].Position = table ? at + 1 : positions[at];
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return RouteResult.Done(Read(entity));
    }

    public async Task<RouteBasic> ReadBasicAsync(CancellationToken ct)
    {
        var rows = await TableAsync(ct).ConfigureAwait(false);
        return new RouteBasic
        {
            Direct = rows.FirstOrDefault(rule => rule.Id == RouteBasic.DirectId)?.Targets ?? [],
            Block = rows.FirstOrDefault(rule => rule.Id == RouteBasic.BlockId)?.Targets ?? [],
            UpdatedUtc = rows.Select(rule => rule.UpdatedUtc).DefaultIfEmpty().Max(),
        };
    }

    public Task<RouteFault?> SaveBasicAsync(RouteBasic basic, CancellationToken ct) => _db.AloneAsync(async () =>
    {
        if (RouteRules.CheckBasic(basic) is { } fault)
        {
            return fault;
        }
        foreach (var draft in basic.Rules())
        {
            var held = await FindAsync(draft.Id, ct).ConfigureAwait(false);
            if (held is null)
            {
                await ImportExceptionAsync(draft, ct).ConfigureAwait(false);
            }
            else if (!held.Targets.SequenceEqual(draft.Targets))
            {
                await ChangeCoreAsync(held.Id, held with { Targets = draft.Targets, RuleIds = [] }, ct).ConfigureAwait(false);
            }
        }
        foreach (var id in new[] { RouteBasic.DirectId, RouteBasic.BlockId })
        {
            if (!basic.Rules().Any(rule => rule.Id == id))
            {
                await RemoveAsync(id, ct).ConfigureAwait(false);
            }
        }
        return default(RouteFault);
    }, ct);

    private async Task<RouteResult?> UnknownRuleAsync(IReadOnlyList<long> rules, CancellationToken ct)
    {
        if (rules.Distinct().Count() != rules.Count)
        {
            return RouteResult.No(RouteOutcome.Invalid, "duplicate-traffic-rule", "a routing entry repeats a traffic rule");
        }
        var known = await _db.TrafficRules.Where(rule => rules.Contains(rule.Id)).Select(rule => rule.Id).ToListAsync(ct).ConfigureAwait(false);
        return rules.Any(id => !known.Contains(id))
            ? RouteResult.No(RouteOutcome.Invalid, "unknown-traffic-rule", "the routing entry names an unknown traffic rule")
            : null;
    }

    private async Task<RouteRule> WithCriteriaAsync(RouteRule rule, CancellationToken ct)
    {
        if (rule.RuleIds.Count == 0)
        {
            return rule;
        }
        var held = await _db.TrafficRules.AsNoTracking().Where(one => rule.RuleIds.Contains(one.Id)).ToListAsync(ct).ConfigureAwait(false);
        var known = held.ToDictionary(one => one.Id);
        var criteria = rule.RuleIds.Where(known.ContainsKey).Select(id => TrafficRuleStore.Read(known[id])).ToArray();
        return rule with
        {
            Criteria = criteria,
            Targets = [.. criteria.SelectMany(one => one.Targets).Distinct(StringComparer.Ordinal)],
        };
    }

    private static RouteFault? Check(RouteRule rule) =>
        RouteBasic.Holds(rule.Id)
            ? RouteRules.CheckBasic(new RouteBasic { Direct = rule.Targets })
                ?? RouteRules.Check(rule with { Targets = [] })
            : RouteRules.Check(rule);

    private async Task<int> NextPositionAsync(CancellationToken ct)
    {
        var rows = await _db.Rules.Include(row => row.Matches).ThenInclude(match => match.Rule).OrderBy(row => row.Position).ThenBy(row => row.Id).ToListAsync(ct).ConfigureAwait(false);
        var lastRow = rows.LastOrDefault();
        var fallback = lastRow is not null && lastRow.Action == RouteAction.Direct
            && lastRow.Outbound.Length == 0 && lastRow.Protocol == RouteProtocol.Any
            && Parts(lastRow.Clients).Count == 0 && Parts(lastRow.Inbounds).Count == 0
            && Parts(lastRow.Ports).Count == 0 && Parts(lastRow.SourcePorts).Count == 0
            && lastRow.Matches.Count == 1
            && lastRow.Matches.Single().Rule.DefaultKey == "resolver-default"
            && Parts(lastRow.Matches.Single().Rule.Targets).Count == 0 ? lastRow : null;
        var last = rows.Where(row => row != fallback).Select(row => row.Position).DefaultIfEmpty(0).Max();
        if (last < int.MaxValue - 1)
        {
            return Math.Max(last, 0) + 1;
        }
        var position = 1;
        foreach (var row in rows.Where(row => row != fallback))
        {
            row.Position = position++;
        }
        if (fallback is not null)
        {
            fallback.Position = position + 1;
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return position;
    }

    /// <summary>
    /// Returns why the client or the endpoint under the name cannot be named by the rules on the list and by no other,
    /// or null when it can.
    /// </summary>
    public async Task<RouteResult?> RefuseNamingAsync(RuleSide side, string name, IReadOnlyCollection<long> rules, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var held = await TableAsync(ct).ConfigureAwait(false);
        var known = held.Select(rule => rule.Id).ToHashSet();
        var strangers = rules.Where(id => !known.Contains(id)).ToArray();
        if (strangers.Length > 0)
        {
            return Missing(strangers[0]);
        }

        foreach (var rule in held)
        {
            var names = rule.Of(side);
            var named = names.Contains(name, side.Comparer());
            var takes = rules.Contains(rule.Id);
            if (takes && !named && names.Count == 0)
            {
                return RouteResult.No(
                    RouteOutcome.Invalid,
                    "rule-for-everyone",
                    $"the rule '{rule.Name}' matches every one, naming '{name}' in it would narrow it");
            }

            if (!takes && named && names.Count == 1)
            {
                return RouteResult.No(
                    RouteOutcome.Invalid,
                    "rule-keeps-last",
                    $"'{name}' is the last one the rule '{rule.Name}' names, without it the rule would match every one");
            }
        }

        return null;
    }

    /// <summary>
    /// Names the client or the endpoint in the rules on the list and takes the name off the other rules.
    /// </summary>
    public async Task NameInAsync(RuleSide side, string name, IReadOnlyCollection<long> rules, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var now = _time.GetUtcNow();
        var comparer = side.Comparer();
        foreach (var entity in await _db.Rules.ToListAsync(ct).ConfigureAwait(false))
        {
            var names = Read(entity).Of(side);
            var takes = rules.Contains(entity.Id);
            if (names.Contains(name, comparer) == takes)
            {
                continue;
            }

            var next = string.Join(", ", takes ? names.Append(name) : names.Where(one => !comparer.Equals(one, name)));
            if (side == RuleSide.Clients)
            {
                entity.Clients = next;
            }
            else
            {
                entity.Inbounds = next;
            }

            entity.UpdatedUtc = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns why the rules on the list cannot leave through the outbound or the balancer under the name while the
    /// rules that leave through it keep doing so, or null when they can.
    /// </summary>
    public async Task<RouteResult?> RefuseLeavingAsync(string way, IReadOnlyCollection<long> rules, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var held = await TableAsync(ct).ConfigureAwait(false);
        var known = held.Select(rule => rule.Id).ToHashSet();
        var strangers = rules.Where(id => !known.Contains(id)).ToArray();
        if (strangers.Length > 0)
        {
            return Missing(strangers[0]);
        }

        foreach (var rule in held)
        {
            var outward = rule.Action == RouteAction.Out;
            var takes = rules.Contains(rule.Id);
            if (outward && !takes && string.Equals(rule.Outbound, way, StringComparison.Ordinal))
            {
                return RouteResult.No(
                    RouteOutcome.Invalid,
                    "way-keeps-rules",
                    $"the rule '{rule.Name}' leaves through '{way}', another way out is picked in the rule");
            }

            if (takes && !outward)
            {
                return RouteResult.No(
                    RouteOutcome.Invalid,
                    "rule-not-out",
                    $"the rule '{rule.Name}' sends its traffic through no outbound");
            }
        }

        return null;
    }

    /// <summary>
    /// Sends the rules on the list out through the outbound or the balancer under the name.
    /// </summary>
    public async Task LeaveThroughAsync(string way, IReadOnlyCollection<long> rules, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var now = _time.GetUtcNow();
        var wanted = rules.Distinct().ToList();
        var found = await _db.Rules.Where(rule => wanted.Contains(rule.Id)).ToListAsync(ct).ConfigureAwait(false);
        foreach (var entity in found.Where(entity => !string.Equals(entity.Outbound, way, StringComparison.Ordinal)))
        {
            entity.Outbound = way;
            entity.UpdatedUtc = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static RouteResult Missing(long id) =>
        RouteResult.No(RouteOutcome.Unknown, "unknown-rule", $"there is no rule under the number {id}");

    private static RouteResult Taken(string name) =>
        RouteResult.No(RouteOutcome.NameTaken, "name-taken", $"the panel already carries a rule called '{name}'");

    private static RouteRule Read(RoutingEntryEntity entity)
    {
        var criteria = entity.Matches.OrderBy(match => match.Position).Select(match => TrafficRuleStore.Read(match.Rule)).ToArray();
        return new RouteRule
        {
            Id = entity.Id,
            Name = entity.Name,
            Position = entity.Position,
            IsEnabled = entity.IsEnabled,
            Action = entity.Action,
            Outbound = entity.Outbound,
            RuleIds = [.. criteria.Select(rule => rule.Id)],
            Criteria = criteria,
            Targets = [.. criteria.SelectMany(rule => rule.Targets).Distinct(StringComparer.Ordinal)],
            Clients = Parts(entity.Clients),
            Inbounds = Parts(entity.Inbounds),
            Ports = Parts(entity.Ports),
            SourcePorts = Parts(entity.SourcePorts),
            Protocol = entity.Protocol,
            CreatedUtc = entity.CreatedUtc,
            UpdatedUtc = entity.UpdatedUtc,
        };
    }

    private async Task WriteAsync(RoutingEntryEntity entity, RouteRule rule, CancellationToken ct)
    {
        var ids = rule.RuleIds;
        if (ids.Count == 0)
        {
            var store = new TrafficRuleStore(_db, _time);
            var name = rule.Name.Trim();
            var suffix = 1;
            while (await _db.TrafficRules.AnyAsync(one => one.Name == name, ct).ConfigureAwait(false))
            {
                name = rule.Name.Trim()[..Math.Min(rule.Name.Trim().Length, 24)] + "-" + suffix++;
            }
            var made = await store.AddAsync(new TrafficRule { Name = name, Targets = rule.Targets, Dns = AmneziaGeo.Server.Routing.Dns.DnsDefaults.Upstreams }, ct).ConfigureAwait(false);
            if (!made.IsOk)
            {
                throw new InvalidOperationException(made.Message);
            }
            ids = [made.Record!.Id];
        }
        var same = entity.Matches.OrderBy(match => match.Position).Select(match => match.RuleId).SequenceEqual(ids);
        if (!same)
        {
            _db.RoutingEntryRules.RemoveRange(entity.Matches);
            entity.Matches.Clear();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            var found = await _db.TrafficRules.Where(one => ids.Contains(one.Id)).ToDictionaryAsync(one => one.Id, ct).ConfigureAwait(false);
            for (var at = 0; at < ids.Count; at++)
            {
                entity.Matches.Add(new RoutingEntryRuleEntity { RuleId = ids[at], Position = at, Rule = found[ids[at]] });
            }
        }
        entity.Name = rule.Name.Trim();
        entity.Position = rule.Position;
        entity.IsEnabled = rule.IsEnabled;
        entity.Action = rule.Action;
        entity.Outbound = rule.Outbound.Trim();
        entity.Clients = string.Join(", ", rule.Clients);
        entity.Inbounds = string.Join(", ", rule.Inbounds);
        entity.Ports = string.Join(", ", rule.Ports);
        entity.SourcePorts = string.Join(", ", rule.SourcePorts);
        entity.Protocol = rule.Protocol;
    }

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
