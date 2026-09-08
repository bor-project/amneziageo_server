using AmneziaGeo.Server.Awg.Client;
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
    /// Returns the wanted name, or the first name under it no client of the endpoint carries.
    /// </summary>
    public async Task<string> FreeNameAsync(long configId, string wanted, CancellationToken ct)
    {
        var found = await _db.Clients.AsNoTracking()
            .Where(client => client.ConfigId == configId)
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

        var wanted = Whole(draft);
        if (await Refusal(wanted, 0, ct).ConfigureAwait(false) is { } refusal)
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

    /// <summary>
    /// Replaces the settings of a client.
    /// </summary>
    public async Task<ClientResult> ChangeAsync(long id, TunnelClient draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var entity = await _db.Clients.FirstOrDefaultAsync(client => client.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var wanted = Whole(draft) with { ConfigId = entity.ConfigId };
        if (await Refusal(wanted, id, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        Write(entity, wanted);
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ClientResult.Done(Read(entity));
    }

    /// <summary>
    /// Turns a client on or off.
    /// </summary>
    public async Task<ClientResult> SwitchAsync(long id, bool on, CancellationToken ct)
    {
        var entity = await _db.Clients.FirstOrDefaultAsync(client => client.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        entity.IsEnabled = on;
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ClientResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes a client.
    /// </summary>
    public async Task<ClientResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.Clients.FirstOrDefaultAsync(client => client.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var gone = Read(entity);
        _db.Clients.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ClientResult.Done(gone);
    }

    private async Task<ClientResult?> Refusal(TunnelClient draft, long id, CancellationToken ct)
    {
        if (ClientRules.Check(draft) is { } fault)
        {
            return ClientResult.No(ClientOutcome.Invalid, fault.Code, fault.Message);
        }

        if (!await _db.Configs.AnyAsync(config => config.Id == draft.ConfigId, ct).ConfigureAwait(false))
        {
            return ClientResult.No(
                ClientOutcome.UnknownConfig,
                "unknown-config",
                $"there is no endpoint under the number {draft.ConfigId}");
        }

        if (await _db.Clients.AnyAsync(
                client => client.ConfigId == draft.ConfigId && client.Name == draft.Name && client.Id != id,
                ct)
            .ConfigureAwait(false))
        {
            return ClientResult.No(
                ClientOutcome.NameTaken,
                "client-name-taken",
                $"the endpoint already carries a client called '{draft.Name}'");
        }

        if (await _db.Clients.AnyAsync(client => client.PublicKey == draft.PublicKey && client.Id != id, ct)
            .ConfigureAwait(false))
        {
            return ClientResult.No(
                ClientOutcome.KeyTaken,
                "client-key-taken",
                "another client already carries this public key");
        }

        var held = await _db.Clients.AsNoTracking()
            .Where(client => client.ConfigId == draft.ConfigId && client.Id != id)
            .Select(client => client.Address)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var taken = new HashSet<string>(held.SelectMany(Parts).Select(Bare), StringComparer.OrdinalIgnoreCase);
        foreach (var address in draft.Address.Select(Bare))
        {
            if (taken.Contains(address))
            {
                return ClientResult.No(
                    ClientOutcome.AddressTaken,
                    "client-address-taken",
                    $"another client of the endpoint already carries {address}");
            }
        }

        return null;
    }

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
    }

    private static TunnelClient Whole(TunnelClient draft) => draft with
    {
        Name = draft.Name.Trim(),
        PrivateKey = draft.PrivateKey.Trim(),
        PublicKey = Key(draft),
        PresharedKey = draft.PresharedKey.Trim(),
        Note = draft.Note.Trim(),
    };

    private static string Key(TunnelClient client) =>
        Curve25519.IsKey(client.PrivateKey) ? Curve25519.PublicOf(client.PrivateKey) : client.PublicKey.Trim();

    private static string Bare(string address)
    {
        var mark = address.IndexOf('/', StringComparison.Ordinal);

        return mark < 0 ? address : address[..mark];
    }

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
