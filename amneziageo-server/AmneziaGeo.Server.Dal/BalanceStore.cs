using AmneziaGeo.Server.Routing.Balance;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What managing a balancer produced.
/// </summary>
public enum BalanceOutcome
{
    Ok = 0,
    Invalid = 1,
    NameTaken = 2,
    Unknown = 3,
}

/// <summary>
/// The balancer a command produced, and why it was refused.
/// </summary>
public sealed record BalanceResult(BalanceOutcome Outcome, string Code, string Message, Balancer? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == BalanceOutcome.Ok;

    /// <summary>
    /// Returns the balancer a command produced.
    /// </summary>
    public static BalanceResult Done(Balancer balancer) =>
        new(BalanceOutcome.Ok, string.Empty, string.Empty, balancer);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static BalanceResult No(BalanceOutcome outcome, string code, string message) =>
        new(outcome, code, message, null);
}

/// <summary>
/// Adds, changes and removes the balancers the panel holds.
/// </summary>
public sealed class BalanceStore
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public BalanceStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every balancer the panel holds, in the order they are read in.
    /// </summary>
    public async Task<IReadOnlyList<Balancer>> ListAsync(CancellationToken ct)
    {
        var found = await _db.Balancers
            .AsNoTracking()
            .OrderBy(balancer => balancer.Position)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns one balancer, or null when the panel holds none under the number.
    /// </summary>
    public async Task<Balancer?> FindAsync(long id, CancellationToken ct)
    {
        var found = await _db.Balancers
            .AsNoTracking()
            .FirstOrDefaultAsync(balancer => balancer.Id == id, ct)
            .ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Adds a balancer, giving it the place at the end.
    /// </summary>
    public async Task<BalanceResult> AddAsync(Balancer draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await TakenAsync(draft.Name, 0, ct).ConfigureAwait(false))
        {
            return Taken(draft.Name);
        }

        var full = draft with { Position = await NextPositionAsync(ct).ConfigureAwait(false) };
        if (BalanceRules.Check(full) is { } fault)
        {
            return BalanceResult.No(BalanceOutcome.Invalid, fault.Code, fault.Message);
        }

        var now = _time.GetUtcNow();
        var entity = new BalancerEntity { CreatedUtc = now, UpdatedUtc = now };
        Write(entity, full);

        _db.Balancers.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return BalanceResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the settings of a balancer, keeping its place.
    /// </summary>
    public async Task<BalanceResult> ChangeAsync(long id, Balancer draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var entity = await _db.Balancers
            .FirstOrDefaultAsync(balancer => balancer.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return Missing(id);
        }

        if (await TakenAsync(draft.Name, id, ct).ConfigureAwait(false))
        {
            return Taken(draft.Name);
        }

        var full = draft with { Position = entity.Position };
        if (BalanceRules.Check(full) is { } fault)
        {
            return BalanceResult.No(BalanceOutcome.Invalid, fault.Code, fault.Message);
        }

        Write(entity, full);
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return BalanceResult.Done(Read(entity));
    }

    /// <summary>
    /// Turns a balancer on or off.
    /// </summary>
    public async Task<BalanceResult> SwitchAsync(long id, bool on, CancellationToken ct)
    {
        var entity = await _db.Balancers
            .FirstOrDefaultAsync(balancer => balancer.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return Missing(id);
        }

        entity.IsEnabled = on;
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return BalanceResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes a balancer, unless a rule leaves through it.
    /// </summary>
    public async Task<BalanceResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.Balancers
            .FirstOrDefaultAsync(balancer => balancer.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return Missing(id);
        }

        var name = entity.Name;
        if (await _db.Rules.AnyAsync(rule => rule.Outbound == name, ct).ConfigureAwait(false))
        {
            return BalanceResult.No(
                BalanceOutcome.Invalid,
                "balancer-in-use",
                $"a rule leaves through the balancer '{name}'");
        }

        var gone = Read(entity);
        _db.Balancers.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return BalanceResult.Done(gone);
    }

    private async Task<bool> TakenAsync(string name, long id, CancellationToken ct)
    {
        var body = name.Trim();
        if (await _db.Balancers.AnyAsync(one => one.Name == body && one.Id != id, ct).ConfigureAwait(false))
        {
            return true;
        }

        return await _db.Outbounds.AnyAsync(one => one.Name == body, ct).ConfigureAwait(false);
    }

    private async Task<int> NextPositionAsync(CancellationToken ct)
    {
        var last = await _db.Balancers
            .MaxAsync(balancer => (int?)balancer.Position, ct)
            .ConfigureAwait(false);

        return (last ?? 0) + 1;
    }

    private static BalanceResult Missing(long id) =>
        BalanceResult.No(BalanceOutcome.Unknown, "unknown-balancer", $"there is no balancer under the number {id}");

    private static BalanceResult Taken(string name) =>
        BalanceResult.No(BalanceOutcome.NameTaken, "name-taken", $"the panel already carries '{name}'");

    private static Balancer Read(BalancerEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Position = entity.Position,
        IsEnabled = entity.IsEnabled,
        Strategy = entity.Strategy,
        Members = Parts(entity.Members),
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static void Write(BalancerEntity entity, Balancer balancer)
    {
        entity.Name = balancer.Name.Trim();
        entity.Position = balancer.Position;
        entity.IsEnabled = balancer.IsEnabled;
        entity.Strategy = balancer.Strategy;
        entity.Members = string.Join(", ", balancer.Members);
    }

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
