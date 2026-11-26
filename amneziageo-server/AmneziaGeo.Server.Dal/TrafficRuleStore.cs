using System.Text.Json;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Returns a traffic rule operation.
/// </summary>
public sealed record TrafficRuleResult(TrafficRule? Record, RouteFault? Fault)
{
    public bool IsOk => Record is not null && Fault is null;
    public string Code => Fault?.Code ?? string.Empty;
    public string Message => Fault?.Message ?? string.Empty;
}

/// <summary>
/// Stores reusable traffic rules.
/// </summary>
public sealed class TrafficRuleStore
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];
    private readonly AppDbContext _db;
    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public TrafficRuleStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<TrafficRule>> ListAsync(CancellationToken ct) =>
        [.. (await _db.TrafficRules.AsNoTracking().OrderBy(rule => rule.Id).ToListAsync(ct).ConfigureAwait(false)).Select(Read)];

    public async Task<TrafficRule?> FindAsync(long id, CancellationToken ct)
    {
        var held = await _db.TrafficRules.AsNoTracking().FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
        return held is null ? null : Read(held);
    }

    public Task<TrafficRuleResult> AddAsync(TrafficRule draft, CancellationToken ct) => SaveAsync(null, draft, ct);

    public Task<TrafficRuleResult> ChangeAsync(long id, TrafficRule draft, CancellationToken ct) => SaveAsync(id, draft, ct);

    public Task<TrafficRuleResult> RemoveAsync(long id, CancellationToken ct) => _db.AloneAsync(async () =>
    {
        var held = await _db.TrafficRules.FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Missing();
        }
        if (await _db.RoutingEntryRules.AnyAsync(match => match.RuleId == id, ct).ConfigureAwait(false))
        {
            return new TrafficRuleResult(null, new RouteFault("traffic-rule-in-use", "routing entries reference the traffic rule"));
        }
        _db.TrafficRules.Remove(held);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new TrafficRuleResult(Read(held), null);
    }, ct);

    private Task<TrafficRuleResult> SaveAsync(long? id, TrafficRule draft, CancellationToken ct) => _db.AloneAsync(async () =>
    {
        if (RouteRules.Check(draft) is { } fault)
        {
            return new TrafficRuleResult(null, fault);
        }
        var name = draft.Name.Trim();
        if (await _db.TrafficRules.AnyAsync(rule => rule.Name == name && rule.Id != id, ct).ConfigureAwait(false))
        {
            return new TrafficRuleResult(null, new RouteFault("name-taken", "a traffic rule already has this name"));
        }
        var held = id is null ? null : await _db.TrafficRules.FirstOrDefaultAsync(rule => rule.Id == id, ct).ConfigureAwait(false);
        if (id is not null && held is null)
        {
            return Missing();
        }
        if (draft.DefaultKey == RouteDefaults.InternetOnly
            && ((held?.DefaultKey is { } key && key != draft.DefaultKey)
                || await _db.TrafficRules.AnyAsync(rule => rule.DefaultKey == draft.DefaultKey && rule.Id != id, ct).ConfigureAwait(false)))
        {
            return new TrafficRuleResult(null, new RouteFault("routing-profile-exists", "the routing profile already has an address set"));
        }

        var now = _time.GetUtcNow();
        var entity = held ?? new TrafficRuleEntity { CreatedUtc = now };
        if (draft.DefaultKey == RouteDefaults.InternetOnly)
        {
            entity.DefaultKey = draft.DefaultKey;
        }

        entity.Name = name;
        entity.Targets = string.Join(", ", draft.Targets);
        entity.DnsMode = draft.DnsMode;
        entity.Dns = string.Join(", ", draft.Dns);
        entity.DnsMembers = JsonSerializer.Serialize(draft.DnsMembers);
        entity.UpdatedUtc = now;
        if (held is null)
        {
            _db.TrafficRules.Add(entity);
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new TrafficRuleResult(Read(entity), null);
    }, ct);

    public static TrafficRule Read(TrafficRuleEntity entity) => new()
    {
        Id = entity.Id,
        DefaultKey = entity.DefaultKey,
        Name = entity.Name,
        Targets = Parts(entity.Targets),
        DnsMode = entity.DnsMode,
        Dns = Parts(entity.Dns),
        DnsMembers = JsonSerializer.Deserialize<Dictionary<string, IReadOnlyList<string>>>(entity.DnsMembers) ?? [],
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static TrafficRuleResult Missing() => new(null, new RouteFault("unknown-traffic-rule", "the traffic rule does not exist"));
}
