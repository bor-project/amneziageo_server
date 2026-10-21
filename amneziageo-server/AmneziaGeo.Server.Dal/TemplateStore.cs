using System.Globalization;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
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
    /// Returns the clients that take each template, by number.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, IReadOnlyList<long>>> MembersAsync(CancellationToken ct)
    {
        var found = await _db.Clients.AsNoTracking()
            .Where(client => client.TemplateId != null)
            .OrderBy(client => client.Id)
            .Select(client => new { client.Id, client.TemplateId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return found.GroupBy(client => client.TemplateId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<long>)group.Select(client => client.Id).ToArray());
    }

    /// <summary>
    /// Returns the clients that take no template, by number.
    /// </summary>
    public async Task<IReadOnlyList<long>> PlainAsync(CancellationToken ct) =>
        await _db.Clients.AsNoTracking()
            .Where(client => client.TemplateId == null)
            .OrderBy(client => client.Id)
            .Select(client => client.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

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
    public Task<TemplateResult> AddAsync(ClientTemplate draft, CancellationToken ct) => AddAsync(draft, null, ct);

    /// <summary>
    /// Adds a template with the settings it carries and hands it to the clients, none to hand it to nobody.
    /// </summary>
    public async Task<TemplateResult> AddAsync(ClientTemplate draft, IReadOnlyCollection<long>? clients, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var wanted = Whole(draft);
        if (await RefusalAsync(wanted, 0, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        if (await StrangerAsync(clients, ct).ConfigureAwait(false) is { } stranger)
        {
            return stranger;
        }

        var now = _time.GetUtcNow();
        var entity = new TemplateEntity { CreatedUtc = now, UpdatedUtc = now };
        Write(entity, wanted);

        await _db.AloneAsync(
            async () =>
            {
                _db.Templates.Add(entity);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                await HandAsync(entity.Id, clients, now, ct).ConfigureAwait(false);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);

        return TemplateResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the settings of a template.
    /// </summary>
    public Task<TemplateResult> ChangeAsync(long id, ClientTemplate draft, CancellationToken ct) =>
        ChangeAsync(id, draft, null, ct);

    /// <summary>
    /// Replaces the settings of a template and the clients that take it, none to leave the clients as they are.
    /// </summary>
    public async Task<TemplateResult> ChangeAsync(
        long id,
        ClientTemplate draft,
        IReadOnlyCollection<long>? clients,
        CancellationToken ct)
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

        if (await StrangerAsync(clients, ct).ConfigureAwait(false) is { } stranger)
        {
            return stranger;
        }

        var now = _time.GetUtcNow();
        Write(entity, wanted);
        entity.UpdatedUtc = now;
        await HandAsync(id, clients, now, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TemplateResult.Done(Read(entity));
    }

    /// <summary>
    /// Writes the ranges of a template worked out anew, unless the template was changed after it was read, and tells
    /// whether it wrote them.
    /// </summary>
    public async Task<bool> RenewAsync(ClientTemplate renewed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(renewed);

        var entity = await _db.Templates
            .FirstOrDefaultAsync(template => template.Id == renewed.Id, ct)
            .ConfigureAwait(false);
        if (entity is null || entity.UpdatedUtc != renewed.UpdatedUtc)
        {
            return false;
        }

        entity.AllowedIps = string.Join(", ", renewed.AllowedIps);
        entity.Missed = string.Join(", ", renewed.Missed);
        entity.RefreshedUtc = renewed.RefreshedUtc;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Takes the templates off the clients and returns why it refused, or null when it went through.
    /// </summary>
    public async Task<TemplateResult?> FreeAsync(IReadOnlyCollection<long> clients, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clients);

        if (await StrangerAsync(clients, ct).ConfigureAwait(false) is { } stranger)
        {
            return stranger;
        }

        var wanted = clients.Distinct().ToArray();
        var kept = await _db.Clients.AsNoTracking()
            .Where(client => client.TemplateId == null && !wanted.Contains(client.Id))
            .OrderBy(client => client.Id)
            .Select(client => client.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (kept is not null)
        {
            return TemplateResult.No(
                TemplateOutcome.Invalid,
                "default-keeps-clients",
                $"the client '{kept}' takes no template and gets one in the form of that template");
        }

        var now = _time.GetUtcNow();
        var moved = await _db.Clients
            .Where(client => client.TemplateId != null && wanted.Contains(client.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var client in moved)
        {
            client.TemplateId = null;
            client.UpdatedUtc = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return null;
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

        if (await MissingPresetAsync(draft.Presets, ct).ConfigureAwait(false) is { } missing)
        {
            return TemplateResult.No(
                TemplateOutcome.Invalid,
                "unknown-preset",
                $"there is no routing preset under the number {missing}");
        }

        return null;
    }

    // Returns the first preset number the panel holds nothing under, or null when it holds them all.
    private async Task<long?> MissingPresetAsync(IReadOnlyList<long> presets, CancellationToken ct)
    {
        if (presets.Count == 0)
        {
            return null;
        }

        var named = presets.ToList();
        var held = await _db.RoutingPresets.Where(preset => named.Contains(preset.Id))
            .Select(preset => preset.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return named.Where(id => !held.Contains(id)).Select(id => (long?)id).FirstOrDefault();
    }

    // Returns the refusal of a list that names a client the panel does not hold, or null when it holds them all.
    private async Task<TemplateResult?> StrangerAsync(IReadOnlyCollection<long>? clients, CancellationToken ct)
    {
        if (clients is null || clients.Count == 0)
        {
            return null;
        }

        var wanted = clients.Distinct().ToArray();
        var held = await _db.Clients.AsNoTracking()
            .Where(client => wanted.Contains(client.Id))
            .Select(client => client.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var missing = wanted.Except(held).ToArray();

        return missing.Length == 0
            ? null
            : TemplateResult.No(TemplateOutcome.Invalid, "unknown-client", $"there is no client under the number {missing[0]}");
    }

    // Hands the template to the clients on the list and takes it off the other clients that took it.
    private async Task HandAsync(long id, IReadOnlyCollection<long>? clients, DateTimeOffset now, CancellationToken ct)
    {
        if (clients is null)
        {
            return;
        }

        var wanted = clients.Distinct().ToArray();
        var touched = await _db.Clients
            .Where(client => client.TemplateId == id || wanted.Contains(client.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var client in touched)
        {
            var takes = wanted.Contains(client.Id) ? id : (long?)null;
            if (client.TemplateId != takes)
            {
                client.TemplateId = takes;
                client.UpdatedUtc = now;
            }
        }
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
        DnsMode = (DnsChoice)entity.DnsMode,
        Mtu = entity.Mtu,
        Keepalive = entity.Keepalive,
        StandIns = entity.StandIns,
        Presets = Numbers(entity.Presets),
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
        entity.DnsMode = (int)template.DnsMode;
        entity.Mtu = template.Mtu;
        entity.Keepalive = template.Keepalive;
        entity.StandIns = template.StandIns;
        entity.Presets = string.Join(", ", template.Presets);
        entity.RefreshedUtc = template.RefreshedUtc;
    }

    private static ClientTemplate Whole(ClientTemplate draft) => draft with
    {
        Name = draft.Name.Trim(),
        Entries = [.. draft.Entries.Select(one => TemplateList.Entry(one) ?? one.Trim()).Distinct(StringComparer.Ordinal)],
        Presets = [.. draft.Presets.Distinct()],
    };

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static IReadOnlyList<long> Numbers(string text) =>
        [.. Parts(text).Select(part => long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Where(id => id > 0)];
}
