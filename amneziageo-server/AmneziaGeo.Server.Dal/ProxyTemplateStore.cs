using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// The proxy template a command produced, and why it was refused.
/// </summary>
public sealed record ProxyTemplateResult(TemplateOutcome Outcome, string Code, string Message, ProxyTemplate? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == TemplateOutcome.Ok;

    /// <summary>
    /// Returns the template a command produced.
    /// </summary>
    public static ProxyTemplateResult Done(ProxyTemplate template) =>
        new(TemplateOutcome.Ok, string.Empty, string.Empty, template);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static ProxyTemplateResult No(TemplateOutcome outcome, string code, string message) =>
        new(outcome, code, message, null);
}

/// <summary>
/// Adds, changes and removes the proxy templates the panel holds.
/// </summary>
public sealed class ProxyTemplateStore
{
    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyTemplateStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every proxy template the panel holds, by name.
    /// </summary>
    public async Task<IReadOnlyList<ProxyTemplate>> ListAsync(CancellationToken ct)
    {
        var found = await _db.ProxyTemplates.AsNoTracking()
            .OrderBy(template => template.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns one template, or null when the panel holds none under the number.
    /// </summary>
    public async Task<ProxyTemplate?> FindAsync(long id, CancellationToken ct)
    {
        var found = await _db.ProxyTemplates.AsNoTracking()
            .FirstOrDefaultAsync(template => template.Id == id, ct)
            .ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Returns the template under a name, or null when the panel holds none.
    /// </summary>
    public async Task<ProxyTemplate?> FindByNameAsync(string name, CancellationToken ct)
    {
        var found = await _db.ProxyTemplates.AsNoTracking()
            .FirstOrDefaultAsync(template => template.Name == name, ct)
            .ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Returns how many proxies take each template.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, int>> UsesAsync(CancellationToken ct)
    {
        var found = await _db.Proxy.AsNoTracking()
            .Where(row => row.TemplateId != null)
            .Select(row => row.TemplateId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return found.OfType<long>().GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
    }

    /// <summary>
    /// Returns why a template cannot be written under the number, or null when it can.
    /// </summary>
    public Task<ProxyTemplateResult?> RefuseAsync(ProxyTemplate draft, long id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return RefusalAsync(Whole(draft), id, ct);
    }

    /// <summary>
    /// Adds a template with the settings it carries.
    /// </summary>
    public async Task<ProxyTemplateResult> AddAsync(ProxyTemplate draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var wanted = Whole(draft);
        if (await RefusalAsync(wanted, 0, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        var now = _time.GetUtcNow();
        var entity = new ProxyTemplateEntity { CreatedUtc = now, UpdatedUtc = now };
        Write(entity, wanted);

        _db.ProxyTemplates.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ProxyTemplateResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the settings of a template.
    /// </summary>
    public async Task<ProxyTemplateResult> ChangeAsync(long id, ProxyTemplate draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var entity = await _db.ProxyTemplates.FirstOrDefaultAsync(template => template.Id == id, ct)
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

        return ProxyTemplateResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes a template, unless a proxy takes it.
    /// </summary>
    public async Task<ProxyTemplateResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.ProxyTemplates.FirstOrDefaultAsync(template => template.Id == id, ct)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        if (await _db.Proxy.AnyAsync(row => row.TemplateId == id, ct).ConfigureAwait(false))
        {
            return ProxyTemplateResult.No(
                TemplateOutcome.InUse,
                "template-in-use",
                $"a proxy takes the template '{entity.Name}'");
        }

        var gone = Read(entity);
        _db.ProxyTemplates.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ProxyTemplateResult.Done(gone);
    }

    /// <summary>
    /// Puts the built in template in place and gives every loose proxy the template of its settings.
    /// </summary>
    public async Task SeedAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (!await _db.ProxyTemplates.AnyAsync(ct).ConfigureAwait(false))
        {
            var entity = new ProxyTemplateEntity { CreatedUtc = now, UpdatedUtc = now };
            Write(entity, ProxyTemplateDefaults.Fresh());
            _db.ProxyTemplates.Add(entity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var loose = await _db.Proxy.Where(row => row.TemplateId == null).ToListAsync(ct).ConfigureAwait(false);
        if (loose.Count == 0)
        {
            return;
        }

        var held = await _db.ProxyTemplates.ToListAsync(ct).ConfigureAwait(false);
        var known = new Dictionary<string, ProxyTemplateEntity>(StringComparer.Ordinal);
        foreach (var one in held)
        {
            known.TryAdd(Signature(Read(one)), one);
        }

        var names = held.Select(one => one.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var row in loose)
        {
            var wanted = Of(row.Name, row);
            if (known.ContainsKey(Signature(wanted)))
            {
                continue;
            }

            var entity = new ProxyTemplateEntity { CreatedUtc = now, UpdatedUtc = now };
            Write(entity, wanted with { Name = Free(row.Name, names) });
            _db.ProxyTemplates.Add(entity);
            names.Add(entity.Name);
            known[Signature(Read(entity))] = entity;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (var row in loose)
        {
            row.TemplateId = known[Signature(Of(row.Name, row))].Id;
            row.UpdatedUtc = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<ProxyTemplateResult?> RefusalAsync(ProxyTemplate draft, long id, CancellationToken ct)
    {
        if (ProxyTemplateRules.Check(draft) is { } fault)
        {
            return ProxyTemplateResult.No(TemplateOutcome.Invalid, fault.Code, fault.Message);
        }

        return await _db.ProxyTemplates.AnyAsync(one => one.Name == draft.Name && one.Id != id, ct)
            .ConfigureAwait(false)
            ? ProxyTemplateResult.No(
                TemplateOutcome.NameTaken,
                "name-taken",
                $"the panel already carries a template called '{draft.Name}'")
            : null;
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

    private static string Signature(ProxyTemplate template) => string.Join(
        '|',
        template.Kind,
        template.Opened,
        template.MakePath,
        template.Target,
        PanelList.Line(PanelList.Of(template.Sources)));

    private static ProxyTemplateResult Missing(long id) => ProxyTemplateResult.No(
        TemplateOutcome.Unknown,
        "unknown-template",
        $"there is no proxy template under the number {id}");

    private static ProxyTemplate Of(string name, ProxyEntity row) => new()
    {
        Name = name,
        Kind = row.Kind,
        Port = row.Port,
        Opened = row.Opened,
        MakePath = ProxyKind.HasPath(row.Kind),
        Target = row.Target,
        Sources = PanelList.Split(row.Sources),
    };

    private static ProxyTemplate Read(ProxyTemplateEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Kind = entity.Kind,
        Port = entity.Port,
        Opened = entity.Opened,
        MakePath = entity.MakePath,
        Target = entity.Target,
        Sources = PanelList.Split(entity.Sources),
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static void Write(ProxyTemplateEntity entity, ProxyTemplate template)
    {
        entity.Name = template.Name;
        entity.Kind = template.Kind;
        entity.Port = template.Port;
        entity.Opened = template.Opened;
        entity.MakePath = template.MakePath;
        entity.Target = template.Target.Trim();
        entity.Sources = PanelList.Line(PanelList.Of(template.Sources));
    }

    private static ProxyTemplate Whole(ProxyTemplate draft) => draft with { Name = draft.Name.Trim() };
}
