using System.Text.Json;
using AmneziaGeo.Server.Awg.Client;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

public sealed partial class TemplateStore
{
    private static readonly JsonSerializerOptions DefaultJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Returns the default client template.
    /// </summary>
    public async Task<ClientTemplate> DefaultAsync(CancellationToken ct) =>
        await EffectiveAsync(null, ct).ConfigureAwait(false) ?? new ClientTemplate { Name = TemplateDefaults.Name, IsDefault = true };

    /// <summary>
    /// Returns the effective template of a client.
    /// </summary>
    public async Task<ClientTemplate?> EffectiveAsync(long? id, CancellationToken ct)
    {
        if (id is { } chosen)
        {
            return await FindAsync(chosen, ct).ConfigureAwait(false);
        }

        var payload = await _db.DefaultTemplates.AsNoTracking().Where(one => one.Id == 1)
            .Select(one => one.Payload).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return payload is null ? null : JsonSerializer.Deserialize<ClientTemplate>(payload, DefaultJson)
            ?? throw new InvalidDataException("the default client template is empty");
    }

    /// <summary>
    /// Returns the effective client templates.
    /// </summary>
    public async Task<IReadOnlyList<ClientTemplate>> EffectiveListAsync(CancellationToken ct)
    {
        var templates = await ListAsync(ct).ConfigureAwait(false);
        var fallback = await EffectiveAsync(null, ct).ConfigureAwait(false);
        return fallback is null ? templates : [fallback, .. templates];
    }

    /// <summary>
    /// Validates the default client template.
    /// </summary>
    public Task<TemplateResult?> RefuseDefaultAsync(ClientTemplate draft, CancellationToken ct) =>
        RefusalAsync(Whole(draft), 0, ct, defaultTemplate: true);

    /// <summary>
    /// Replaces the default client template.
    /// </summary>
    public Task<TemplateResult> ChangeDefaultAsync(ClientTemplate draft, IReadOnlyCollection<long>? clients, CancellationToken ct) =>
        _db.AloneAsync(async () =>
        {
            if (draft.Name != TemplateDefaults.Name)
            {
                return TemplateResult.No(TemplateOutcome.Invalid, "default-name-fixed", "the default template name is fixed");
            }

            var wanted = Whole(draft);
            if (await RefuseDefaultAsync(wanted, ct).ConfigureAwait(false) is { } refusal)
            {
                return refusal;
            }

            if (clients is not null && await FreeAsync(clients, ct).ConfigureAwait(false) is { } membership)
            {
                return membership;
            }

            var held = await DefaultAsync(ct).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            wanted = wanted with { Id = 0, IsDefault = true, CreatedUtc = held.CreatedUtc == default ? now : held.CreatedUtc, UpdatedUtc = now };
            var entity = await _db.DefaultTemplates.SingleOrDefaultAsync(one => one.Id == 1, ct).ConfigureAwait(false);
            if (entity is null)
            {
                entity = new DefaultTemplateEntity { Id = 1 };
                _db.DefaultTemplates.Add(entity);
            }

            entity.Payload = JsonSerializer.Serialize(wanted, DefaultJson);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return TemplateResult.Done(wanted);
        }, ct);

    /// <summary>
    /// Restores the factory client defaults.
    /// </summary>
    public Task ResetDefaultAsync(CancellationToken ct) => _db.AloneAsync(async () =>
    {
        var entity = await _db.DefaultTemplates.SingleOrDefaultAsync(one => one.Id == 1, ct).ConfigureAwait(false);
        if (entity is not null)
        {
            _db.DefaultTemplates.Remove(entity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }, ct);

    private Task<bool> RenewDefaultAsync(ClientTemplate renewed, CancellationToken ct) => _db.AloneAsync(async () =>
    {
        var held = await EffectiveAsync(null, ct).ConfigureAwait(false);
        if (held is null || held.UpdatedUtc != renewed.UpdatedUtc)
        {
            return false;
        }

        var next = held with { AllowedIps = renewed.AllowedIps, Missed = renewed.Missed, RefreshedUtc = renewed.RefreshedUtc };
        var entity = await _db.DefaultTemplates.SingleAsync(one => one.Id == 1, ct).ConfigureAwait(false);
        entity.Payload = JsonSerializer.Serialize(next, DefaultJson);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }, ct);

    internal async Task AssignDefaultPresetAsync(long preset, bool enabled, DateTimeOffset now, CancellationToken ct)
    {
        var held = await EffectiveAsync(null, ct).ConfigureAwait(false);
        if (held is null && !enabled || held?.Presets.Contains(preset) == enabled)
        {
            return;
        }

        held ??= await DefaultAsync(ct).ConfigureAwait(false);
        var next = held with
        {
            Presets = enabled ? [.. held.Presets, preset] : [.. held.Presets.Where(id => id != preset)],
            CreatedUtc = held.CreatedUtc == default ? now : held.CreatedUtc,
            UpdatedUtc = now,
        };
        next = next with { PresetsOnly = next.PresetsOnly && next.Presets.Count > 0 };
        var entity = await _db.DefaultTemplates.SingleOrDefaultAsync(one => one.Id == 1, ct).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new DefaultTemplateEntity { Id = 1 };
            _db.DefaultTemplates.Add(entity);
        }

        entity.Payload = JsonSerializer.Serialize(next, DefaultJson);
    }

    /// <summary>
    /// Removes references from the default template.
    /// </summary>
    public async Task PruneDefaultAsync(long? proxy, long? preset, CancellationToken ct)
    {
        var held = await EffectiveAsync(null, ct).ConfigureAwait(false);
        if (held is null)
        {
            return;
        }

        var next = held with
        {
            ProxyIds = [.. held.ProxyIds.Where(id => id != proxy)],
            Presets = [.. held.Presets.Where(id => id != preset)],
        };
        next = next with { PresetsOnly = next.PresetsOnly && next.Presets.Count > 0 };
        var entity = await _db.DefaultTemplates.SingleAsync(one => one.Id == 1, ct).ConfigureAwait(false);
        entity.Payload = JsonSerializer.Serialize(next, DefaultJson);
    }
}
