using System.Globalization;
using AmneziaGeo.Server.Routing.Template;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What managing a routing preset produced.
/// </summary>
public enum PresetOutcome
{
    Ok = 0,
    Invalid = 1,
    NameTaken = 2,
    Unknown = 3,
    InUse = 4,
}

/// <summary>
/// The preset a command produced, and why it was refused.
/// </summary>
public sealed record PresetResult(PresetOutcome Outcome, string Code, string Message, RoutingPreset? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == PresetOutcome.Ok;

    /// <summary>
    /// Returns the preset a command produced.
    /// </summary>
    public static PresetResult Done(RoutingPreset preset) =>
        new(PresetOutcome.Ok, string.Empty, string.Empty, preset);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static PresetResult No(PresetOutcome outcome, string code, string message) =>
        new(outcome, code, message, null);
}

/// <summary>
/// Adds, changes and removes the routing presets the templates hand to their clients.
/// </summary>
public sealed class PresetStore
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public PresetStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every preset the panel holds, by name.
    /// </summary>
    public async Task<IReadOnlyList<RoutingPreset>> ListAsync(CancellationToken ct)
    {
        var found = await _db.RoutingPresets.AsNoTracking()
            .OrderBy(preset => preset.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns the presets under the numbers in the order they are named, skipping the numbers that hold none.
    /// </summary>
    public async Task<IReadOnlyList<RoutingPreset>> ListAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        var named = ids.ToList();
        var found = await _db.RoutingPresets.AsNoTracking()
            .Where(preset => named.Contains(preset.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var byId = found.ToDictionary(preset => preset.Id);

        return [.. named.Distinct().Where(byId.ContainsKey).Select(id => Read(byId[id]))];
    }

    /// <summary>
    /// Returns one preset, or null when the panel holds none under the number.
    /// </summary>
    public async Task<RoutingPreset?> FindAsync(long id, CancellationToken ct)
    {
        var found = await _db.RoutingPresets.AsNoTracking()
            .FirstOrDefaultAsync(preset => preset.Id == id, ct)
            .ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Returns how many templates name each preset.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, int>> UsesAsync(CancellationToken ct)
    {
        var lists = await _db.Templates.AsNoTracking()
            .Select(template => template.Presets)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return lists.SelectMany(list => Numbers(list).Distinct())
            .GroupBy(id => id)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    /// <summary>
    /// Adds a preset with the lists it carries.
    /// </summary>
    public async Task<PresetResult> AddAsync(RoutingPreset draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var wanted = Whole(draft);
        if (await RefusalAsync(wanted, 0, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        var now = _time.GetUtcNow();
        var entity = new RoutingPresetEntity { Uid = PresetRules.FreshUid(), CreatedUtc = now, UpdatedUtc = now };
        Write(entity, wanted);

        _db.RoutingPresets.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return PresetResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the lists of a preset, moving its time when the list it hands out changes.
    /// </summary>
    public async Task<PresetResult> ChangeAsync(long id, RoutingPreset draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var entity = await _db.RoutingPresets.FirstOrDefaultAsync(preset => preset.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var wanted = Whole(draft);
        if (await RefusalAsync(wanted, id, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        var held = Read(entity);
        Write(entity, wanted);
        if (!PresetRules.SameList(held, Read(entity)))
        {
            entity.UpdatedUtc = _time.GetUtcNow();
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return PresetResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes a preset, unless a template names it.
    /// </summary>
    public async Task<PresetResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.RoutingPresets.FirstOrDefaultAsync(preset => preset.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var uses = await UsesAsync(ct).ConfigureAwait(false);
        if (uses.GetValueOrDefault(id) > 0)
        {
            return PresetResult.No(
                PresetOutcome.InUse,
                "preset-in-use",
                $"a template names the routing preset '{entity.Name}'");
        }

        var gone = Read(entity);
        _db.RoutingPresets.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return PresetResult.Done(gone);
    }

    private async Task<PresetResult?> RefusalAsync(RoutingPreset draft, long id, CancellationToken ct)
    {
        if (PresetRules.Check(draft) is { } fault)
        {
            return PresetResult.No(PresetOutcome.Invalid, fault.Code, fault.Message);
        }

        if (await _db.RoutingPresets.AnyAsync(one => one.Name == draft.Name && one.Id != id, ct).ConfigureAwait(false))
        {
            return PresetResult.No(
                PresetOutcome.NameTaken,
                "name-taken",
                $"the panel already carries a routing preset called '{draft.Name}'");
        }

        return null;
    }

    private static PresetResult Missing(long id) =>
        PresetResult.No(PresetOutcome.Unknown, "unknown-preset", $"there is no routing preset under the number {id}");

    private static RoutingPreset Read(RoutingPresetEntity entity) => new()
    {
        Id = entity.Id,
        Uid = entity.Uid,
        Name = entity.Name,
        Proxy = Entries(entity.Proxy),
        Direct = Entries(entity.Direct),
        Block = Entries(entity.Block),
        AllUdp = entity.AllUdp,
        Full = entity.Full,
        IsDefault = entity.IsDefault,
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static void Write(RoutingPresetEntity entity, RoutingPreset preset)
    {
        entity.Name = preset.Name;
        entity.Proxy = string.Join(", ", preset.Proxy);
        entity.Direct = string.Join(", ", preset.Direct);
        entity.Block = string.Join(", ", preset.Block);
        entity.AllUdp = preset.AllUdp;
        entity.Full = preset.Full;
        entity.IsDefault = preset.IsDefault;
    }

    private static RoutingPreset Whole(RoutingPreset draft) => draft with
    {
        Name = draft.Name.Trim(),
        Proxy = Clean(draft.Proxy),
        Direct = Clean(draft.Direct),
        Block = Clean(draft.Block),
    };

    private static IReadOnlyList<string> Clean(IReadOnlyList<string> entries) =>
        [.. entries.Select(one => TemplateList.Entry(one) ?? one.Trim()).Distinct(StringComparer.Ordinal)];

    private static IReadOnlyList<string> Entries(string text) =>
        [.. Parts(text).Select(one => TemplateList.Entry(one) ?? one)];

    private static IReadOnlyList<long> Numbers(string text) =>
        [.. Parts(text).Select(part => long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Where(id => id > 0)];

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
