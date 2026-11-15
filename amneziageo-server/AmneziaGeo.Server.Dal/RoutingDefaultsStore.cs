using System.Globalization;
using AmneziaGeo.Server.Routing.Template;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Counts the restored routing records.
/// </summary>
public sealed record RoutingDefaultsResult(int Groups, int Presets);

/// <summary>
/// Restores missing bundled routing records.
/// </summary>
public sealed class RoutingDefaultsStore
{
    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public RoutingDefaultsStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Restores the bundled address groups.
    /// </summary>
    public Task<RoutingDefaultsResult> RestoreGroupsAsync(CancellationToken ct) => RestoreAsync(true, false, ct);

    /// <summary>
    /// Restores the bundled client presets.
    /// </summary>
    public Task<RoutingDefaultsResult> RestorePresetsAsync(CancellationToken ct) => RestoreAsync(false, true, ct);

    /// <summary>
    /// Restores both bundled collections.
    /// </summary>
    public Task<RoutingDefaultsResult> RestoreAllAsync(CancellationToken ct) => RestoreAsync(true, true, ct);

    private Task<RoutingDefaultsResult> RestoreAsync(bool groups, bool presets, CancellationToken ct) =>
        _db.AloneAsync(async () =>
        {
            var heldGroups = groups ? await _db.AddressGroups.ToListAsync(ct).ConfigureAwait(false) : [];
            var heldPresets = presets ? await _db.RoutingPresets.ToListAsync(ct).ConfigureAwait(false) : [];
            var groupKeys = heldGroups.Select(record => record.DefaultKey).ToHashSet(StringComparer.Ordinal);
            var presetKeys = heldPresets.Select(record => record.DefaultKey).ToHashSet(StringComparer.Ordinal);
            var groupNames = heldGroups.Select(record => record.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var presetNames = heldPresets.Select(record => record.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var now = _time.GetUtcNow();
            var addedGroups = 0;
            var addedPresets = 0;
            foreach (var list in RoutingBundle.Lists)
            {
                if (groups && groupKeys.Add(list.Key))
                {
                    _db.AddressGroups.Add(new AddressGroupEntity
                    {
                        DefaultKey = list.Key,
                        Name = FreeName(list.Name, groupNames),
                        Targets = string.Join(", ", list.Targets),
                        CreatedUtc = now,
                        UpdatedUtc = now,
                    });
                    addedGroups++;
                }

                if (presets && presetKeys.Add(list.Key))
                {
                    _db.RoutingPresets.Add(new RoutingPresetEntity
                    {
                        DefaultKey = list.Key,
                        Uid = PresetRules.FreshUid(),
                        Name = FreeName(list.Name, presetNames),
                        Proxy = string.Join('\n', list.Targets.Concat(list.Applications)),
                        IsDefault = true,
                        CreatedUtc = now,
                        UpdatedUtc = now,
                    });
                    addedPresets++;
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            return new RoutingDefaultsResult(addedGroups, addedPresets);
        }, ct);

    private static string FreeName(string name, HashSet<string> names)
    {
        if (names.Add(name))
        {
            return name;
        }

        for (var number = 1; ; number++)
        {
            var suffix = number == 1 ? " (AmneziaGeo)" : $" (AmneziaGeo {number.ToString(CultureInfo.InvariantCulture)})";
            var candidate = name + suffix;
            if (names.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
