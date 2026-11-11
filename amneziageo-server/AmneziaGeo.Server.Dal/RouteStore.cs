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
    private const long BasicRow = 1;

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
            .AsNoTracking()
            .Where(rule => rule.Id > 0)
            .OrderBy(rule => rule.Position)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns the complete rule order.
    /// </summary>
    public async Task<IReadOnlyList<RouteRule>> TableAsync(CancellationToken ct)
    {
        var found = await _db.Rules.AsNoTracking().OrderBy(rule => rule.Position).ToListAsync(ct).ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns the address groups.
    /// </summary>
    public async Task<IReadOnlyList<AddressGroup>> GroupsAsync(CancellationToken ct)
    {
        var found = await _db.AddressGroups.AsNoTracking().OrderBy(group => group.Id).ToListAsync(ct).ConfigureAwait(false);

        return [.. found.Select(AddressGroupStore.Read)];
    }

    /// <summary>
    /// Imports an address group.
    /// </summary>
    public async Task<AddressGroupResult> ImportGroupAsync(AddressGroup draft, CancellationToken ct)
    {
        var store = new AddressGroupStore(_db, _time);
        var held = await _db.AddressGroups.AsNoTracking().FirstOrDefaultAsync(group => group.Name == draft.Name, ct).ConfigureAwait(false);

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

            var held = await _db.Rules.FirstOrDefaultAsync(rule => rule.Id == draft.Id, ct).ConfigureAwait(false);
            if (held is not null)
            {
                return await ChangeCoreAsync(draft.Id, draft, ct).ConfigureAwait(false);
            }

            if (Check(draft) is { } fault)
            {
                return RouteResult.No(RouteOutcome.Invalid, fault.Code, fault.Message);
            }

            if (await UnknownGroupAsync(draft.Groups, ct).ConfigureAwait(false) is { } missing)
            {
                return missing;
            }

            if (await _db.Rules.AnyAsync(rule => rule.Name == draft.Name, ct).ConfigureAwait(false))
            {
                return Taken(draft.Name);
            }

            var now = _time.GetUtcNow();
            var entity = new RouteRuleEntity { Id = draft.Id, CreatedUtc = now, UpdatedUtc = now };
            Write(entity, draft);
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

    private async Task<RouteResult> AddCoreAsync(RouteRule draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await _db.Rules.AnyAsync(rule => rule.Name == draft.Name, ct).ConfigureAwait(false))
        {
            return Taken(draft.Name);
        }

        var full = draft with { Id = 0, Position = await NextPositionAsync(ct).ConfigureAwait(false) };
        if (Check(full) is { } fault)
        {
            return RouteResult.No(RouteOutcome.Invalid, fault.Code, fault.Message);
        }

        if (await UnknownGroupAsync(full.Groups, ct).ConfigureAwait(false) is { } missing)
        {
            return missing;
        }

        var now = _time.GetUtcNow();
        var entity = new RouteRuleEntity { CreatedUtc = now, UpdatedUtc = now };
        Write(entity, full);

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

        var entity = await _db.Rules.FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        if (await _db.Rules.AnyAsync(rule => rule.Name == draft.Name && rule.Id != id, ct).ConfigureAwait(false))
        {
            return Taken(draft.Name);
        }

        var full = draft with { Id = id, Position = entity.Position };
        if (Check(full) is { } fault)
        {
            return RouteResult.No(RouteOutcome.Invalid, fault.Code, fault.Message);
        }

        if (await UnknownGroupAsync(full.Groups, ct).ConfigureAwait(false) is { } missing)
        {
            return missing;
        }

        Write(entity, full);
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return RouteResult.Done(Read(entity));
    }

    /// <summary>
    /// Turns a rule on or off.
    /// </summary>
    public async Task<RouteResult> SwitchAsync(long id, bool on, CancellationToken ct)
    {
        var entity = await _db.Rules.FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
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
        var entity = await _db.Rules.FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
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
        var entity = await _db.Rules.FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
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
        var all = await _db.Rules.OrderBy(rule => rule.Position).ToListAsync(ct).ConfigureAwait(false);
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

    /// <summary>
    /// Returns the basic lists the panel holds.
    /// </summary>
    public async Task<RouteBasic> ReadBasicAsync(CancellationToken ct)
    {
        var held = await _db.RouteBasics.AsNoTracking().FirstOrDefaultAsync(row => row.Id == BasicRow, ct)
            .ConfigureAwait(false);
        var rules = await _db.Rules.AsNoTracking().Where(rule => rule.Id == RouteBasic.BlockId || rule.Id == RouteBasic.DirectId)
            .ToListAsync(ct).ConfigureAwait(false);
        var prior = held is null ? RouteBasic.Empty : Read(held);

        return prior with
        {
            Direct = rules.FirstOrDefault(rule => rule.Id == RouteBasic.DirectId) is { } direct ? Parts(direct.Targets) : prior.Direct,
            Block = rules.FirstOrDefault(rule => rule.Id == RouteBasic.BlockId) is { } block ? Parts(block.Targets) : prior.Block,
            UpdatedUtc = rules.Select(rule => rule.UpdatedUtc).Append(prior.UpdatedUtc).Max(),
        };
    }

    /// <summary>
    /// Saves the basic lists, returning why they were refused, or null when they went through.
    /// </summary>
    public async Task<RouteFault?> SaveBasicAsync(RouteBasic basic, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(basic);

        if (RouteRules.CheckBasic(basic) is { } fault)
        {
            return fault;
        }

        await MigrateBasicAsync(ct).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        await WriteBasicAsync(RouteBasic.BlockId, RouteBasic.BlockName, RouteAction.Block, basic.Block, int.MinValue, now, ct).ConfigureAwait(false);
        await WriteBasicAsync(RouteBasic.DirectId, RouteBasic.DirectName, RouteAction.Direct, basic.Direct, int.MinValue + 1, now, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// Moves legacy exceptions into the rule order.
    /// </summary>
    public async Task MigrateBasicAsync(CancellationToken ct)
    {
        await _db.AloneAsync(async () =>
        {
            var held = await _db.RouteBasics.FirstOrDefaultAsync(row => row.Id == BasicRow, ct).ConfigureAwait(false);
            if (held is null || (held.Direct.Length == 0 && held.Block.Length == 0))
            {
                return;
            }

            var before = Read(held).Rules().Concat(await TableAsync(ct).ConfigureAwait(false)).ToArray();
            foreach (var rule in Read(held).Rules())
            {
                if (await _db.Rules.AnyAsync(one => one.Id == rule.Id, ct).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("the legacy routing identifier is already assigned");
                }

                var entity = new RouteRuleEntity { Id = rule.Id, CreatedUtc = held.UpdatedUtc, UpdatedUtc = held.UpdatedUtc };
                Write(entity, rule with { Name = await BasicNameAsync(rule.Name, ct).ConfigureAwait(false) });
                _db.Rules.Add(entity);
            }

            held.Direct = string.Empty;
            held.Block = string.Empty;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (!RouteCompatibility.Same(before, await TableAsync(ct).ConfigureAwait(false)))
            {
                throw new InvalidOperationException("the routing upgrade changed rule conditions or priority");
            }
        }, ct).ConfigureAwait(false);
    }

    private async Task<string> BasicNameAsync(string name, CancellationToken ct)
    {
        var candidate = name;
        var suffix = 1;
        while (await _db.Rules.AnyAsync(rule => rule.Name == candidate, ct).ConfigureAwait(false))
        {
            candidate = name + "-" + suffix++;
        }

        return candidate;
    }

    private async Task WriteBasicAsync(long id, string name, string action, IReadOnlyList<string> targets, int position, DateTimeOffset now, CancellationToken ct)
    {
        var held = await _db.Rules.FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
        if (held is not null && Parts(held.Targets).SequenceEqual(targets))
        {
            return;
        }

        if (targets.Count == 0)
        {
            if (held is not null && held.Groups.Length == 0)
            {
                _db.Rules.Remove(held);
            }
            else if (held is not null)
            {
                held.Targets = string.Empty;
                held.UpdatedUtc = now;
            }

            return;
        }

        if (held is null)
        {
            held = new RouteRuleEntity
            {
                Id = id,
                Name = await BasicNameAsync(name, ct).ConfigureAwait(false),
                Position = position,
                Action = action,
                IsEnabled = true,
                Protocol = RouteProtocol.Any,
                CreatedUtc = now,
            };
            _db.Rules.Add(held);
        }

        held.Targets = string.Join(", ", targets);
        held.UpdatedUtc = now;
    }

    private async Task<RouteResult?> UnknownGroupAsync(IReadOnlyList<long> groups, CancellationToken ct)
    {
        var known = await _db.AddressGroups.Where(group => groups.Contains(group.Id)).Select(group => group.Id).ToListAsync(ct).ConfigureAwait(false);

        return groups.Any(id => !known.Contains(id))
            ? RouteResult.No(RouteOutcome.Invalid, "unknown-address-group", "the rule names an unknown address group")
            : null;
    }

    private static RouteFault? Check(RouteRule rule) =>
        RouteBasic.Holds(rule.Id)
            ? RouteRules.CheckBasic(new RouteBasic { Direct = rule.Targets })
                ?? RouteRules.Check(rule with { Targets = [] })
            : RouteRules.Check(rule);

    private async Task<int> NextPositionAsync(CancellationToken ct)
    {
        var last = await _db.Rules.MaxAsync(rule => (int?)rule.Position, ct).ConfigureAwait(false);

        return Math.Max(last ?? 0, 0) + 1;
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

    private static RouteRule Read(RouteRuleEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Position = entity.Position,
        IsEnabled = entity.IsEnabled,
        Action = entity.Action,
        Outbound = entity.Outbound,
        Targets = Parts(entity.Targets),
        Groups = GroupIds(entity.Groups),
        Clients = Parts(entity.Clients),
        Inbounds = Parts(entity.Inbounds),
        Ports = Parts(entity.Ports),
        SourcePorts = Parts(entity.SourcePorts),
        Protocol = entity.Protocol,
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static RouteBasic Read(RouteBasicEntity entity) => new()
    {
        Direct = Parts(entity.Direct),
        Block = Parts(entity.Block),
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static void Write(RouteRuleEntity entity, RouteRule rule)
    {
        entity.Name = rule.Name.Trim();
        entity.Position = rule.Position;
        entity.IsEnabled = rule.IsEnabled;
        entity.Action = rule.Action;
        entity.Outbound = rule.Outbound.Trim();
        entity.Targets = string.Join(", ", rule.Targets);
        entity.Groups = string.Join(", ", rule.Groups);
        entity.Clients = string.Join(", ", rule.Clients);
        entity.Inbounds = string.Join(", ", rule.Inbounds);
        entity.Ports = string.Join(", ", rule.Ports);
        entity.SourcePorts = string.Join(", ", rule.SourcePorts);
        entity.Protocol = rule.Protocol;
    }

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Reads group identifiers.
    /// </summary>
    public static IReadOnlyList<long> GroupIds(string text) =>
        [.. Parts(text).Select(part => long.TryParse(part, out var id) ? id : 0)];
}
