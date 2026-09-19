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
            .OrderBy(rule => rule.Position)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

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
    public async Task<RouteResult> AddAsync(RouteRule draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await _db.Rules.AnyAsync(rule => rule.Name == draft.Name, ct).ConfigureAwait(false))
        {
            return Taken(draft.Name);
        }

        var full = draft with { Position = await NextPositionAsync(ct).ConfigureAwait(false) };
        if (RouteRules.Check(full) is { } fault)
        {
            return RouteResult.No(RouteOutcome.Invalid, fault.Code, fault.Message);
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
    public async Task<RouteResult> ChangeAsync(long id, RouteRule draft, CancellationToken ct)
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

        var full = draft with { Position = entity.Position };
        if (RouteRules.Check(full) is { } fault)
        {
            return RouteResult.No(RouteOutcome.Invalid, fault.Code, fault.Message);
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
    public async Task<RouteResult> MoveAsync(long id, bool up, CancellationToken ct)
    {
        var entity = await _db.Rules.FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var neighbour = up
            ? await _db.Rules.Where(one => one.Position < entity.Position)
                .OrderByDescending(one => one.Position)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
            : await _db.Rules.Where(one => one.Position > entity.Position)
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
    public async Task<RouteResult> PlaceAsync(long id, int place, CancellationToken ct)
    {
        var all = await _db.Rules.OrderBy(rule => rule.Position).ToListAsync(ct).ConfigureAwait(false);
        var entity = all.FirstOrDefault(rule => rule.Id == id);
        if (entity is null)
        {
            return Missing(id);
        }

        all.Remove(entity);
        all.Insert(Math.Clamp(place, 1, all.Count + 1) - 1, entity);
        for (var at = 0; at < all.Count; at++)
        {
            all[at].Position = at + 1;
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

        return held is null ? RouteBasic.Empty : Read(held);
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

        var held = await _db.RouteBasics.FirstOrDefaultAsync(row => row.Id == BasicRow, ct).ConfigureAwait(false);
        if (held is null)
        {
            held = new RouteBasicEntity { Id = BasicRow };
            _db.RouteBasics.Add(held);
        }

        held.Direct = string.Join(", ", basic.Direct);
        held.Block = string.Join(", ", basic.Block);
        held.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return null;
    }

    private async Task<int> NextPositionAsync(CancellationToken ct)
    {
        var last = await _db.Rules.MaxAsync(rule => (int?)rule.Position, ct).ConfigureAwait(false);

        return (last ?? 0) + 1;
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
        HoldsWhenDown = entity.HoldsWhenDown,
        Targets = Parts(entity.Targets),
        Sources = Parts(entity.Sources),
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
        entity.HoldsWhenDown = rule.HoldsWhenDown;
        entity.Targets = string.Join(", ", rule.Targets);
        entity.Sources = string.Join(", ", rule.Sources);
        entity.Clients = string.Join(", ", rule.Clients);
        entity.Inbounds = string.Join(", ", rule.Inbounds);
        entity.Ports = string.Join(", ", rule.Ports);
        entity.SourcePorts = string.Join(", ", rule.SourcePorts);
        entity.Protocol = rule.Protocol;
    }

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
