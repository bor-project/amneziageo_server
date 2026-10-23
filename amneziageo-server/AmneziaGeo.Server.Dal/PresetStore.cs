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
    /// Returns the templates that name each preset, by number.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, IReadOnlyList<long>>> HoldersAsync(CancellationToken ct)
    {
        var found = await _db.Templates.AsNoTracking()
            .OrderBy(template => template.Id)
            .Select(template => new { template.Id, template.Presets })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return found.SelectMany(template => Numbers(template.Presets).Distinct().Select(preset => (Preset: preset, Template: template.Id)))
            .GroupBy(pair => pair.Preset)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<long>)group.Select(pair => pair.Template).ToArray());
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
    public Task<PresetResult> AddAsync(RoutingPreset draft, CancellationToken ct) => AddAsync(draft, null, ct);

    /// <summary>
    /// Adds a preset with the lists it carries and puts it last in the presets of the templates, none for no template.
    /// </summary>
    public async Task<PresetResult> AddAsync(RoutingPreset draft, IReadOnlyCollection<long>? templates, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var wanted = Whole(draft);
        if (await RefusalAsync(wanted, 0, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        if (await StrangerAsync(templates, ct).ConfigureAwait(false) is { } stranger)
        {
            return stranger;
        }

        var now = _time.GetUtcNow();
        var entity = new RoutingPresetEntity { Uid = PresetRules.FreshUid(), CreatedUtc = now, UpdatedUtc = now };
        Write(entity, wanted);

        await _db.AloneAsync(
            async () =>
            {
                _db.RoutingPresets.Add(entity);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                await HandAsync(entity.Id, templates, now, ct).ConfigureAwait(false);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);

        return PresetResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the lists of a preset, moving its time when the list it hands out changes.
    /// </summary>
    public Task<PresetResult> ChangeAsync(long id, RoutingPreset draft, CancellationToken ct) =>
        ChangeAsync(id, draft, null, ct);

    /// <summary>
    /// Replaces the lists of a preset and the templates that name it, none to leave the templates as they are.
    /// </summary>
    public async Task<PresetResult> ChangeAsync(
        long id,
        RoutingPreset draft,
        IReadOnlyCollection<long>? templates,
        CancellationToken ct)
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

        if (await StrangerAsync(templates, ct).ConfigureAwait(false) is { } stranger)
        {
            return stranger;
        }

        var now = _time.GetUtcNow();
        var held = Read(entity);
        Write(entity, wanted);
        if (!PresetRules.SameList(held, Read(entity)))
        {
            entity.UpdatedUtc = now;
        }

        await HandAsync(id, templates, now, ct).ConfigureAwait(false);
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

    // Returns the refusal of a list that names a template the panel does not hold, or null when it holds them all.
    private async Task<PresetResult?> StrangerAsync(IReadOnlyCollection<long>? templates, CancellationToken ct)
    {
        if (templates is null || templates.Count == 0)
        {
            return null;
        }

        var wanted = templates.Distinct().ToArray();
        var held = await _db.Templates.AsNoTracking()
            .Where(template => wanted.Contains(template.Id))
            .Select(template => template.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var missing = wanted.Except(held).ToArray();

        return missing.Length == 0
            ? null
            : PresetResult.No(PresetOutcome.Invalid, "unknown-template", $"there is no template under the number {missing[0]}");
    }

    // Puts the preset last in the presets of the templates on the list and takes it off the other templates that name it;
    // a template left with no preset no longer keeps its clients to its presets.
    private async Task HandAsync(long id, IReadOnlyCollection<long>? templates, DateTimeOffset now, CancellationToken ct)
    {
        if (templates is null)
        {
            return;
        }

        var wanted = templates.Distinct().ToArray();
        var held = await _db.Templates.ToListAsync(ct).ConfigureAwait(false);
        foreach (var template in held)
        {
            var presets = Numbers(template.Presets);
            var takes = wanted.Contains(template.Id);
            if (presets.Contains(id) == takes)
            {
                continue;
            }

            var next = (takes ? presets.Append(id) : presets.Where(preset => preset != id)).ToList();
            template.Presets = string.Join(", ", next);
            template.PresetsOnly &= next.Count > 0;
            template.UpdatedUtc = now;
        }
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
        ReadOnly = entity.ReadOnly,
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static void Write(RoutingPresetEntity entity, RoutingPreset preset)
    {
        entity.Name = preset.Name;
        entity.Proxy = string.Join('\n', preset.Proxy);
        entity.Direct = string.Join('\n', preset.Direct);
        entity.Block = string.Join('\n', preset.Block);
        entity.AllUdp = preset.AllUdp;
        entity.Full = preset.Full;
        entity.IsDefault = preset.IsDefault;
        entity.ReadOnly = preset.ReadOnly;
    }

    private static RoutingPreset Whole(RoutingPreset draft) => draft with
    {
        Name = draft.Name.Trim(),
        Proxy = Clean(draft.Proxy),
        Direct = Clean(draft.Direct),
        Block = Clean(draft.Block),
    };

    private static IReadOnlyList<string> Clean(IReadOnlyList<string> entries) =>
        [.. entries.Select(one => PresetList.Entry(one) ?? one.Trim()).Distinct(StringComparer.Ordinal)];

    // Reads the entries of a list, one a line.
    private static IReadOnlyList<string> Entries(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(one => PresetList.Entry(one) ?? one)];

    private static IReadOnlyList<long> Numbers(string text) =>
        [.. Parts(text).Select(part => long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Where(id => id > 0)];

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
