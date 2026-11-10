using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Returns an address group operation.
/// </summary>
public sealed record AddressGroupResult(AddressGroup? Record, RouteFault? Fault);

/// <summary>
/// Stores address groups.
/// </summary>
public sealed class AddressGroupStore
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public AddressGroupStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the address groups.
    /// </summary>
    public async Task<IReadOnlyList<AddressGroup>> ListAsync(CancellationToken ct)
    {
        var found = await _db.AddressGroups.AsNoTracking().OrderBy(group => group.Id).ToListAsync(ct).ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Adds an address group.
    /// </summary>
    public Task<AddressGroupResult> AddAsync(AddressGroup draft, CancellationToken ct) =>
        SaveAsync(null, draft, ct);

    /// <summary>
    /// Updates an address group.
    /// </summary>
    public Task<AddressGroupResult> ChangeAsync(long id, AddressGroup draft, CancellationToken ct) =>
        SaveAsync(id, draft, ct);

    /// <summary>
    /// Removes an unused address group.
    /// </summary>
    public Task<AddressGroupResult> RemoveAsync(long id, CancellationToken ct) =>
        _db.AloneAsync(async () =>
        {
            var held = await _db.AddressGroups.FirstOrDefaultAsync(group => group.Id == id, ct).ConfigureAwait(false);
            if (held is null)
            {
                return Missing();
            }

            var linked = await _db.Rules.AsNoTracking().Select(rule => rule.Groups).ToListAsync(ct).ConfigureAwait(false);
            if (linked.Any(groups => RouteStore.GroupIds(groups).Contains(id)))
            {
                return new AddressGroupResult(null, new RouteFault("address-group-in-use", "routing rules reference the address group"));
            }

            _db.AddressGroups.Remove(held);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            return new AddressGroupResult(Read(held), null);
        }, ct);

    private Task<AddressGroupResult> SaveAsync(long? id, AddressGroup draft, CancellationToken ct) =>
        _db.AloneAsync(async () =>
        {
            if (RouteRules.CheckGroup(draft) is { } fault)
            {
                return new AddressGroupResult(null, fault);
            }

            if (await _db.AddressGroups.AnyAsync(group => group.Name == draft.Name.Trim() && group.Id != id, ct).ConfigureAwait(false))
            {
                return new AddressGroupResult(null, new RouteFault("name-taken", "an address group already has this name"));
            }

            var held = id is null ? null : await _db.AddressGroups.FirstOrDefaultAsync(group => group.Id == id, ct).ConfigureAwait(false);
            if (id is not null && held is null)
            {
                return Missing();
            }

            var now = _time.GetUtcNow();
            var entity = held ?? new AddressGroupEntity { CreatedUtc = now };
            entity.Name = draft.Name.Trim();
            entity.Targets = string.Join(", ", draft.Targets);
            entity.Dns = string.Join(", ", draft.Dns);
            entity.UpdatedUtc = now;
            if (held is null)
            {
                _db.AddressGroups.Add(entity);
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            return new AddressGroupResult(Read(entity), null);
        }, ct);

    /// <summary>
    /// Reads an address group.
    /// </summary>
    public static AddressGroup Read(AddressGroupEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Targets = [.. entity.Targets.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        Dns = [.. entity.Dns.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static AddressGroupResult Missing() =>
        new(null, new RouteFault("unknown-address-group", "the address group does not exist"));
}
