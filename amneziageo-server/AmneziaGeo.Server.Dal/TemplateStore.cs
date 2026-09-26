using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Routing.Template;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What managing a client template produced.
/// </summary>
public enum TemplateOutcome
{
    Ok = 0,
    Invalid = 1,
    NameTaken = 2,
    Unknown = 3,
    InUse = 4,
}

/// <summary>
/// The template a command produced, and why it was refused.
/// </summary>
public sealed record TemplateResult(TemplateOutcome Outcome, string Code, string Message, ClientTemplate? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == TemplateOutcome.Ok;

    /// <summary>
    /// Returns the template a command produced.
    /// </summary>
    public static TemplateResult Done(ClientTemplate template) =>
        new(TemplateOutcome.Ok, string.Empty, string.Empty, template);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static TemplateResult No(TemplateOutcome outcome, string code, string message) =>
        new(outcome, code, message, null);
}

/// <summary>
/// Adds, changes and removes the client templates the panel holds.
/// </summary>
public sealed class TemplateStore
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public TemplateStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every template the panel holds, by name.
    /// </summary>
    public async Task<IReadOnlyList<ClientTemplate>> ListAsync(CancellationToken ct)
    {
        var found = await _db.Templates.AsNoTracking()
            .OrderBy(template => template.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns one template, or null when the panel holds none under the number.
    /// </summary>
    public async Task<ClientTemplate?> FindAsync(long id, CancellationToken ct)
    {
        var found = await _db.Templates.AsNoTracking()
            .FirstOrDefaultAsync(template => template.Id == id, ct)
            .ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Returns how many clients take each template.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, int>> UsesAsync(CancellationToken ct)
    {
        var found = await _db.Clients.AsNoTracking()
            .Where(client => client.TemplateId != null)
            .Select(client => client.TemplateId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return found.OfType<long>().GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
    }

    /// <summary>
    /// Puts the built in template in place, once, in a fresh database.
    /// </summary>
    public Task SeedAsync(CancellationToken ct) => _db.AloneAsync(() => AddBuiltInAsync(ct), ct);

    // Adds the built in template when the database holds no template.
    private async Task AddBuiltInAsync(CancellationToken ct)
    {
        if (await _db.Templates.AnyAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        var now = _time.GetUtcNow();
        var entity = new TemplateEntity { CreatedUtc = now, UpdatedUtc = now };
        Write(entity, TemplateDefaults.Fresh());

        _db.Templates.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns why a template cannot be written under the number, or null when it can.
    /// </summary>
    public Task<TemplateResult?> RefuseAsync(ClientTemplate draft, long id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return RefusalAsync(Whole(draft), id, ct);
    }

    /// <summary>
    /// Adds a template with the settings it carries.
    /// </summary>
    public async Task<TemplateResult> AddAsync(ClientTemplate draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var wanted = Whole(draft);
        if (await RefusalAsync(wanted, 0, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        var now = _time.GetUtcNow();
        var entity = new TemplateEntity { CreatedUtc = now, UpdatedUtc = now };
        Write(entity, wanted);

        _db.Templates.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TemplateResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the settings of a template.
    /// </summary>
    public async Task<TemplateResult> ChangeAsync(long id, ClientTemplate draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var entity = await _db.Templates.FirstOrDefaultAsync(template => template.Id == id, ct).ConfigureAwait(false);
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

        return TemplateResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes a template, unless a client takes it.
    /// </summary>
    public async Task<TemplateResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.Templates.FirstOrDefaultAsync(template => template.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        if (await _db.Clients.AnyAsync(client => client.TemplateId == id, ct).ConfigureAwait(false))
        {
            return TemplateResult.No(
                TemplateOutcome.InUse,
                "template-in-use",
                $"a client takes the template '{entity.Name}'");
        }

        var gone = Read(entity);
        _db.Templates.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TemplateResult.Done(gone);
    }

    private async Task<TemplateResult?> RefusalAsync(ClientTemplate draft, long id, CancellationToken ct)
    {
        if ((TemplateRules.Check(draft) ?? TemplateList.Check(draft.Entries)) is { } fault)
        {
            return TemplateResult.No(TemplateOutcome.Invalid, fault.Code, fault.Message);
        }

        if (await _db.Templates.AnyAsync(one => one.Name == draft.Name && one.Id != id, ct).ConfigureAwait(false))
        {
            return TemplateResult.No(
                TemplateOutcome.NameTaken,
                "name-taken",
                $"the panel already carries a template called '{draft.Name}'");
        }

        return null;
    }

    private static TemplateResult Missing(long id) =>
        TemplateResult.No(TemplateOutcome.Unknown, "unknown-template", $"there is no template under the number {id}");

    private static ClientTemplate Read(TemplateEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Entries = [.. Parts(entity.Entries).Select(one => TemplateList.Entry(one) ?? one)],
        AllowedIps = Parts(entity.AllowedIps),
        Missed = Parts(entity.Missed),
        Dns = Parts(entity.Dns),
        Mtu = entity.Mtu,
        Keepalive = entity.Keepalive,
        Routing = !entity.LocksRouting,
        RefreshedUtc = entity.RefreshedUtc,
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static void Write(TemplateEntity entity, ClientTemplate template)
    {
        entity.Name = template.Name;
        entity.Entries = string.Join(", ", template.Entries);
        entity.AllowedIps = string.Join(", ", template.AllowedIps);
        entity.Missed = string.Join(", ", template.Missed);
        entity.Dns = string.Join(", ", template.Dns);
        entity.Mtu = template.Mtu;
        entity.Keepalive = template.Keepalive;
        entity.LocksRouting = !template.Routing;
        entity.RefreshedUtc = template.RefreshedUtc;
    }

    private static ClientTemplate Whole(ClientTemplate draft) => draft with
    {
        Name = draft.Name.Trim(),
        Entries = [.. draft.Entries.Select(one => TemplateList.Entry(one) ?? one.Trim()).Distinct(StringComparer.Ordinal)],
    };

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
