using AmneziaGeo.Server.Awg.Config;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// The endpoint template a command produced, and why it was refused.
/// </summary>
public sealed record InterfaceTemplateResult(
    TemplateOutcome Outcome,
    string Code,
    string Message,
    InterfaceTemplate? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == TemplateOutcome.Ok;

    /// <summary>
    /// Returns the template a command produced.
    /// </summary>
    public static InterfaceTemplateResult Done(InterfaceTemplate template) =>
        new(TemplateOutcome.Ok, string.Empty, string.Empty, template);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static InterfaceTemplateResult No(TemplateOutcome outcome, string code, string message) =>
        new(outcome, code, message, null);
}

/// <summary>
/// Adds, changes and removes the endpoint templates the panel holds.
/// </summary>
public sealed class InterfaceTemplateStore
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public InterfaceTemplateStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every endpoint template the panel holds, by name.
    /// </summary>
    public async Task<IReadOnlyList<InterfaceTemplate>> ListAsync(CancellationToken ct)
    {
        var found = await _db.InterfaceTemplates.AsNoTracking()
            .OrderBy(template => template.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns one template, or null when the panel holds none under the number.
    /// </summary>
    public async Task<InterfaceTemplate?> FindAsync(long id, CancellationToken ct)
    {
        var found = await _db.InterfaceTemplates.AsNoTracking()
            .FirstOrDefaultAsync(template => template.Id == id, ct)
            .ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Returns the template under a name, or null when the panel holds none.
    /// </summary>
    public async Task<InterfaceTemplate?> FindByNameAsync(string name, CancellationToken ct)
    {
        var found = await _db.InterfaceTemplates.AsNoTracking()
            .FirstOrDefaultAsync(template => template.Name == name, ct)
            .ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Returns how many endpoints take each template.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, int>> UsesAsync(CancellationToken ct)
    {
        var found = await _db.Configs.AsNoTracking()
            .Where(config => config.TemplateId != null)
            .Select(config => config.TemplateId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return found.OfType<long>().GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
    }

    /// <summary>
    /// Returns why a template cannot be written under the number, or null when it can.
    /// </summary>
    public Task<InterfaceTemplateResult?> RefuseAsync(InterfaceTemplate draft, long id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return RefusalAsync(Whole(draft), id, ct);
    }

    /// <summary>
    /// Adds a template with the settings it carries.
    /// </summary>
    public async Task<InterfaceTemplateResult> AddAsync(InterfaceTemplate draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var wanted = Whole(draft);
        if (await RefusalAsync(wanted, 0, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        var now = _time.GetUtcNow();
        var entity = new InterfaceTemplateEntity { CreatedUtc = now, UpdatedUtc = now };
        Write(entity, wanted);

        _db.InterfaceTemplates.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return InterfaceTemplateResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the settings of a template.
    /// </summary>
    public async Task<InterfaceTemplateResult> ChangeAsync(long id, InterfaceTemplate draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var entity = await _db.InterfaceTemplates.FirstOrDefaultAsync(template => template.Id == id, ct)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var wanted = Whole(draft);
        if (await RefusalAsync(wanted, id, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        Write(entity, wanted);
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return InterfaceTemplateResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes a template, unless an endpoint takes it.
    /// </summary>
    public async Task<InterfaceTemplateResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.InterfaceTemplates.FirstOrDefaultAsync(template => template.Id == id, ct)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        if (await _db.Configs.AnyAsync(config => config.TemplateId == id, ct).ConfigureAwait(false))
        {
            return InterfaceTemplateResult.No(
                TemplateOutcome.InUse,
                "template-in-use",
                $"an endpoint takes the template '{entity.Name}'");
        }

        var gone = Read(entity);
        _db.InterfaceTemplates.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return InterfaceTemplateResult.Done(gone);
    }

    /// <summary>
    /// Puts the built in template in place and gives every loose endpoint the template of its settings.
    /// </summary>
    public async Task SeedAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (!await _db.InterfaceTemplates.AnyAsync(ct).ConfigureAwait(false))
        {
            var entity = new InterfaceTemplateEntity { CreatedUtc = now, UpdatedUtc = now };
            Write(entity, InterfaceTemplateDefaults.Fresh());
            _db.InterfaceTemplates.Add(entity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var loose = await _db.Configs.Where(config => config.TemplateId == null).ToListAsync(ct).ConfigureAwait(false);
        if (loose.Count == 0)
        {
            return;
        }

        var held = await _db.InterfaceTemplates.ToListAsync(ct).ConfigureAwait(false);
        var known = new Dictionary<string, InterfaceTemplateEntity>(StringComparer.Ordinal);
        foreach (var one in held)
        {
            known.TryAdd(Signature(Read(one)), one);
        }

        var names = held.Select(one => one.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var config in loose)
        {
            var wanted = Of(config.Name, config);
            if (known.ContainsKey(Signature(wanted)))
            {
                continue;
            }

            var entity = new InterfaceTemplateEntity { CreatedUtc = now, UpdatedUtc = now };
            Write(entity, wanted with { Name = Free(config.Name, names) });
            _db.InterfaceTemplates.Add(entity);
            names.Add(entity.Name);
            known[Signature(Read(entity))] = entity;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (var config in loose)
        {
            config.TemplateId = known[Signature(Of(config.Name, config))].Id;
            config.UpdatedUtc = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<InterfaceTemplateResult?> RefusalAsync(InterfaceTemplate draft, long id, CancellationToken ct)
    {
        if (InterfaceTemplateRules.Check(draft) is { } fault)
        {
            return InterfaceTemplateResult.No(TemplateOutcome.Invalid, fault.Code, fault.Message);
        }

        if (await _db.InterfaceTemplates.AnyAsync(one => one.Name == draft.Name && one.Id != id, ct)
            .ConfigureAwait(false))
        {
            return InterfaceTemplateResult.No(
                TemplateOutcome.NameTaken,
                "name-taken",
                $"the panel already carries a template called '{draft.Name}'");
        }

        if (draft.ClientTemplateId is { } client
            && !await _db.Templates.AnyAsync(one => one.Id == client, ct).ConfigureAwait(false))
        {
            return InterfaceTemplateResult.No(
                TemplateOutcome.Unknown,
                "unknown-template",
                $"there is no client template under the number {client}");
        }

        return null;
    }

    private static string Free(string wanted, HashSet<string> taken)
    {
        var name = wanted;
        var next = 2;
        while (taken.Contains(name))
        {
            name = $"{wanted}-{next}";
            next++;
        }

        return name;
    }

    private static string Signature(InterfaceTemplate template) => string.Join(
        '|',
        string.Join(", ", template.Dns),
        string.Join(", ", template.AllowedIps),
        template.Mtu,
        template.Keepalive,
        template.OfflineAfter,
        string.Join(", ", template.Blocked),
        Obfuscations.Line(template.Obfuscation));

    private static InterfaceTemplateResult Missing(long id) => InterfaceTemplateResult.No(
        TemplateOutcome.Unknown,
        "unknown-template",
        $"there is no endpoint template under the number {id}");

    private static InterfaceTemplate Of(string name, ConfigEntity config) => new()
    {
        Name = name,
        ListenPort = config.ListenPort,
        Subnet = Parts(config.Address) is [var first, ..] ? first : ConfigDefaults.Subnet,
        Dns = Parts(config.Dns),
        AllowedIps = Parts(config.AllowedIps),
        Mtu = config.Mtu,
        Keepalive = config.Keepalive,
        OfflineAfter = config.OfflineAfter,
        Blocked = Parts(config.Blocked),
        Obfuscation = Obfuscations.Of(config),
    };

    private static InterfaceTemplate Read(InterfaceTemplateEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        ListenPort = entity.ListenPort,
        Subnet = entity.Subnet,
        Dns = Parts(entity.Dns),
        AllowedIps = Parts(entity.AllowedIps),
        Mtu = entity.Mtu,
        Keepalive = entity.Keepalive,
        OfflineAfter = entity.OfflineAfter,
        Blocked = Parts(entity.Blocked),
        ClientTemplateId = entity.ClientTemplateId,
        Obfuscation = Obfuscations.Of(entity),
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static void Write(InterfaceTemplateEntity entity, InterfaceTemplate template)
    {
        entity.Name = template.Name;
        entity.ListenPort = template.ListenPort;
        entity.Subnet = template.Subnet.Trim();
        entity.Dns = string.Join(", ", template.Dns);
        entity.AllowedIps = string.Join(", ", template.AllowedIps);
        entity.Mtu = template.Mtu;
        entity.Keepalive = template.Keepalive;
        entity.OfflineAfter = template.OfflineAfter;
        entity.Blocked = string.Join(", ", template.Blocked);
        entity.ClientTemplateId = template.ClientTemplateId;
        Obfuscations.Write(entity, template.Obfuscation);
    }

    private static InterfaceTemplate Whole(InterfaceTemplate draft) => draft with { Name = draft.Name.Trim() };

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
