using System.Net;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What managing a client produced.
/// </summary>
public enum ClientOutcome
{
    Ok = 0,
    Invalid = 1,
    NameTaken = 2,
    Unknown = 3,
    KeyTaken = 4,
    AddressTaken = 5,
    UnknownConfig = 6,
    UnknownTemplate = 7,
}

/// <summary>
/// The client a command produced, and why it was refused.
/// </summary>
public sealed record ClientResult(ClientOutcome Outcome, string Code, string Message, TunnelClient? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == ClientOutcome.Ok;

    /// <summary>
    /// Returns the client a command produced.
    /// </summary>
    public static ClientResult Done(TunnelClient client) => new(ClientOutcome.Ok, string.Empty, string.Empty, client);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static ClientResult No(ClientOutcome outcome, string code, string message) =>
        new(outcome, code, message, null);
}

/// <summary>
/// Adds, changes and removes the clients the panel holds.
/// </summary>
public sealed class ClientStore
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public ClientStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every client the panel holds.
    /// </summary>
    public async Task<IReadOnlyList<TunnelClient>> ListAsync(CancellationToken ct)
    {
        var found = await _db.Clients.AsNoTracking()
            .OrderBy(client => client.ConfigId)
            .ThenBy(client => client.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns the clients of one endpoint.
    /// </summary>
    public async Task<IReadOnlyList<TunnelClient>> ListAsync(long configId, CancellationToken ct)
    {
        var found = await _db.Clients.AsNoTracking()
            .Where(client => client.ConfigId == configId)
            .OrderBy(client => client.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns the clients a subscription carries.
    /// </summary>
    public async Task<IReadOnlyList<TunnelClient>> SubscribedAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id))
        {
            return [];
        }

        var found = await _db.Clients.AsNoTracking()
            .Where(client => client.SubscriptionId == id)
            .OrderBy(client => client.ConfigId)
            .ThenBy(client => client.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns the devices of a client.
    /// </summary>
    public async Task<IReadOnlyList<TunnelClient>> DevicesAsync(long parentId, CancellationToken ct)
    {
        var found = await _db.Clients.AsNoTracking()
            .Where(client => client.ParentId == parentId)
            .OrderBy(client => client.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns one client, or null when the panel holds none under the number.
    /// </summary>
    public async Task<TunnelClient?> FindAsync(long id, CancellationToken ct)
    {
        var found = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(client => client.Id == id, ct)
            .ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Returns the addresses the clients of an endpoint already carry.
    /// </summary>
    public async Task<IReadOnlyList<string>> AddressesAsync(long configId, CancellationToken ct)
    {
        var found = await _db.Clients.AsNoTracking()
            .Where(client => client.ConfigId == configId)
            .Select(client => client.Address)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.SelectMany(Parts)];
    }

    /// <summary>
    /// Returns the wanted name, or the first name under it no client of the panel carries.
    /// </summary>
    public async Task<string> FreeNameAsync(string wanted, CancellationToken ct)
    {
        var found = await _db.Clients.AsNoTracking()
            .Select(client => client.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var held = new HashSet<string>(found, StringComparer.OrdinalIgnoreCase);
        if (!held.Contains(wanted))
        {
            return wanted;
        }

        var number = 2;
        while (held.Contains($"{wanted}-{number}"))
        {
            number++;
        }

        return $"{wanted}-{number}";
    }

    /// <summary>
    /// Adds a client with the settings it carries.
    /// </summary>
    public async Task<ClientResult> AddAsync(TunnelClient draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return await InsertAsync(Whole(draft), fit: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a device to a client that takes several, with keys, an address and a subscription of its own.
    /// </summary>
    public async Task<ClientResult> AddDeviceAsync(long parentId, CancellationToken ct)
    {
        var parent = await FindAsync(parentId, ct).ConfigureAwait(false);
        if (parent is null)
        {
            return Missing(parentId);
        }

        if (parent.ParentId is not null)
        {
            return ClientResult.No(ClientOutcome.Invalid, "client-is-device", "a device takes no devices of its own");
        }

        if (!parent.MultiDevice)
        {
            return ClientResult.No(ClientOutcome.Invalid, "client-single-device", "the client is kept to one device");
        }

        var ranges = Parts(await _db.Configs.AsNoTracking()
            .Where(config => config.Id == parent.ConfigId)
            .Select(config => config.Address)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? string.Empty);
        var taken = await AddressesAsync(parent.ConfigId, ct).ConfigureAwait(false);
        var name = await FreeNameAsync(parent.Name, ct).ConfigureAwait(false);
        var device = ClientDefaults.Fresh(parent.ConfigId, name) with
        {
            Address = ClientPool.Free(ranges, [.. taken, .. ranges]),
            PresharedKey = parent.PresharedKey,
            TemplateId = parent.TemplateId,
            IsEnabled = parent.IsEnabled,
            DailyLimit = parent.DailyLimit,
            ParentId = parent.Id,
        };

        return await InsertAsync(Whole(device), fit: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a client a host already carries, under a free name, with the addresses the host gave it and a subscription.
    /// </summary>
    public async Task<ClientResult> ImportAsync(TunnelClient draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var wanted = Subscribed(Whole(draft));
        if (await KeyHeldAsync(wanted.PublicKey, 0, ct).ConfigureAwait(false))
        {
            return KeyTaken();
        }

        var free = await FreeNameAsync(wanted.Name, ct).ConfigureAwait(false);

        return await InsertAsync(wanted with { Name = free }, fit: false, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds the clients a host already carries one by one, passing over the keys the panel holds.
    /// </summary>
    public async Task<ClientImportReport> ImportAllAsync(IReadOnlyList<TunnelClient> drafts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        var taken = 0;
        var held = 0;
        var renamed = new List<ClientRename>();
        var refused = new List<ClientRefusal>();
        foreach (var draft in drafts)
        {
            var result = await ImportAsync(draft, ct).ConfigureAwait(false);
            if (result.IsOk)
            {
                taken++;
                if (!string.Equals(result.Record!.Name, draft.Name.Trim(), StringComparison.Ordinal))
                {
                    renamed.Add(new ClientRename(draft.Name, result.Record.Name));
                }

                continue;
            }

            if (result.Outcome == ClientOutcome.KeyTaken)
            {
                held++;

                continue;
            }

            refused.Add(new ClientRefusal(draft.Name, result.Code, result.Message));
        }

        return new ClientImportReport(taken, held, renamed, refused);
    }

    /// <summary>
    /// Replaces the settings of a client, carrying its state, its template and its daily limit over to its devices.
    /// </summary>
    public async Task<ClientResult> ChangeAsync(long id, TunnelClient draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var entity = await _db.Clients.FirstOrDefaultAsync(client => client.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var wanted = Whole(draft) with
        {
            ConfigId = entity.ConfigId,
            ParentId = entity.ParentId,
            MultiDevice = entity.ParentId is null && draft.MultiDevice,
            DailyLimit = entity.ParentId is null ? draft.DailyLimit : entity.DailyLimit,
        };
        if (entity.MultiDevice && !wanted.MultiDevice
            && await _db.Clients.AnyAsync(client => client.ParentId == id, ct).ConfigureAwait(false))
        {
            return ClientResult.No(ClientOutcome.Invalid, "client-has-devices", "the client still carries devices");
        }

        if (await Refusal(wanted, id, fit: true, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        var now = _time.GetUtcNow();
        Write(entity, wanted);
        entity.UpdatedUtc = now;
        await FollowAsync(id, wanted.IsEnabled, wanted.TemplateId, wanted.DailyLimit, now, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ClientResult.Done(Read(entity));
    }

    /// <summary>
    /// Turns a client on or off together with its devices.
    /// </summary>
    public async Task<ClientResult> SwitchAsync(long id, bool on, CancellationToken ct)
    {
        var entity = await _db.Clients.FirstOrDefaultAsync(client => client.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var now = _time.GetUtcNow();
        entity.IsEnabled = on;
        entity.UpdatedUtc = now;
        await FollowAsync(id, on, entity.TemplateId, entity.DailyLimit, now, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ClientResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes a client together with its devices.
    /// </summary>
    public async Task<ClientResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.Clients.FirstOrDefaultAsync(client => client.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var gone = Read(entity);
        var devices = await _db.Clients.Where(client => client.ParentId == id).ToListAsync(ct).ConfigureAwait(false);
        _db.Clients.RemoveRange(devices);
        _db.Clients.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ClientResult.Done(gone);
    }

    private async Task FollowAsync(
        long parentId,
        bool on,
        long? template,
        long limit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var devices = await _db.Clients.Where(client => client.ParentId == parentId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var device in devices)
        {
            device.IsEnabled = on;
            device.TemplateId = template;
            device.DailyLimit = limit;
            device.UpdatedUtc = now;
        }
    }

    private async Task<ClientResult> InsertAsync(TunnelClient wanted, bool fit, CancellationToken ct)
    {
        if (await Refusal(wanted, 0, fit, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        var now = _time.GetUtcNow();
        var entity = new ClientEntity { ConfigId = wanted.ConfigId, CreatedUtc = now, UpdatedUtc = now };
        Write(entity, wanted);

        _db.Clients.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ClientResult.Done(Read(entity));
    }

    private async Task<ClientResult?> Refusal(TunnelClient draft, long id, bool fit, CancellationToken ct)
    {
        if (ClientRules.Check(draft) is { } fault)
        {
            return ClientResult.No(ClientOutcome.Invalid, fault.Code, fault.Message);
        }

        var ranges = await _db.Configs.AsNoTracking()
            .Where(config => config.Id == draft.ConfigId)
            .Select(config => config.Address)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (ranges is null)
        {
            return ClientResult.No(
                ClientOutcome.UnknownConfig,
                "unknown-config",
                $"there is no endpoint under the number {draft.ConfigId}");
        }

        if (fit && ClientPool.Fit(Parts(ranges), draft.Address) is { } misfit)
        {
            return ClientResult.No(ClientOutcome.Invalid, misfit.Code, misfit.Message);
        }

        if (draft.TemplateId is { } template
            && !await _db.Templates.AnyAsync(one => one.Id == template, ct).ConfigureAwait(false))
        {
            return ClientResult.No(
                ClientOutcome.UnknownTemplate,
                "unknown-template",
                $"there is no template under the number {template}");
        }

        if (await _db.Clients.AnyAsync(
                client => EF.Functions.Collate(client.Name, "NOCASE") == draft.Name && client.Id != id,
                ct)
            .ConfigureAwait(false))
        {
            return ClientResult.No(
                ClientOutcome.NameTaken,
                "client-name-taken",
                $"another client already carries the name '{draft.Name}'");
        }

        if (await KeyHeldAsync(draft.PublicKey, id, ct).ConfigureAwait(false))
        {
            return KeyTaken();
        }

        var held = await _db.Clients.AsNoTracking()
            .Where(client => client.ConfigId == draft.ConfigId && client.Id != id)
            .Select(client => client.Address)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var taken = new HashSet<IPAddress>(held.SelectMany(Parts).Select(Point).OfType<IPAddress>());
        foreach (var address in draft.Address)
        {
            if (Point(address) is { } point && taken.Contains(point))
            {
                return ClientResult.No(
                    ClientOutcome.AddressTaken,
                    "client-address-taken",
                    $"another client of the endpoint already carries {point}");
            }
        }

        return null;
    }

    private Task<bool> KeyHeldAsync(string key, long id, CancellationToken ct) =>
        _db.Clients.AnyAsync(client => client.PublicKey == key && client.Id != id, ct);

    private static ClientResult KeyTaken() =>
        ClientResult.No(ClientOutcome.KeyTaken, "client-key-taken", "another client already carries this public key");

    private static ClientResult Missing(long id) =>
        ClientResult.No(ClientOutcome.Unknown, "unknown-client", $"there is no client under the number {id}");

    private static TunnelClient Read(ClientEntity entity) => new()
    {
        Id = entity.Id,
        ConfigId = entity.ConfigId,
        Name = entity.Name,
        PrivateKey = entity.PrivateKey,
        PublicKey = entity.PublicKey,
        PresharedKey = entity.PresharedKey,
        Address = Parts(entity.Address),
        IsEnabled = entity.IsEnabled,
        Note = entity.Note,
        TemplateId = entity.TemplateId,
        SubscriptionId = entity.SubscriptionId,
        ParentId = entity.ParentId,
        MultiDevice = entity.MultiDevice,
        DailyLimit = entity.DailyLimit,
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
    };

    private static void Write(ClientEntity entity, TunnelClient client)
    {
        entity.Name = client.Name.Trim();
        entity.PrivateKey = client.PrivateKey.Trim();
        entity.PublicKey = client.PublicKey;
        entity.PresharedKey = client.PresharedKey.Trim();
        entity.Address = string.Join(", ", client.Address);
        entity.IsEnabled = client.IsEnabled;
        entity.Note = client.Note.Trim();
        entity.TemplateId = client.TemplateId;
        entity.SubscriptionId = client.SubscriptionId.Trim();
        entity.ParentId = client.ParentId;
        entity.MultiDevice = client.MultiDevice;
        entity.DailyLimit = client.DailyLimit;
    }

    private static TunnelClient Whole(TunnelClient draft) => draft with
    {
        Name = draft.Name.Trim(),
        PrivateKey = draft.PrivateKey.Trim(),
        PublicKey = Key(draft),
        PresharedKey = draft.PresharedKey.Trim(),
        Note = draft.Note.Trim(),
        SubscriptionId = draft.SubscriptionId.Trim(),
    };

    private static TunnelClient Subscribed(TunnelClient client) =>
        client.SubscriptionId.Length > 0 ? client : client with { SubscriptionId = ClientDefaults.SubscriptionId() };

    private static string Key(TunnelClient client) =>
        Curve25519.IsKey(client.PrivateKey) ? Curve25519.PublicOf(client.PrivateKey) : client.PublicKey.Trim();

    private static IPAddress? Point(string address) =>
        AwgAllowedIp.TryParse(address, out var found) ? found.Address : null;

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
