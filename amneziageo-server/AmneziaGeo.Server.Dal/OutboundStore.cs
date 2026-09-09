using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Routing.Outbound;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What managing an outbound produced.
/// </summary>
public enum OutboundOutcome
{
    Ok = 0,
    Invalid = 1,
    NameTaken = 2,
    Unknown = 3,
    NoMark = 4,
}

/// <summary>
/// The outbound a command produced, and why it was refused.
/// </summary>
public sealed record OutboundResult(OutboundOutcome Outcome, string Code, string Message, OutboundConfig? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == OutboundOutcome.Ok;

    /// <summary>
    /// Returns the outbound a command produced.
    /// </summary>
    public static OutboundResult Done(OutboundConfig outbound) =>
        new(OutboundOutcome.Ok, string.Empty, string.Empty, outbound);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static OutboundResult No(OutboundOutcome outcome, string code, string message) =>
        new(outcome, code, message, null);
}

/// <summary>
/// Adds, changes and removes the outbounds the panel holds.
/// </summary>
public sealed class OutboundStore
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public OutboundStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every outbound the panel holds, in the order they are read in.
    /// </summary>
    public async Task<IReadOnlyList<OutboundConfig>> ListAsync(CancellationToken ct)
    {
        var found = await _db.Outbounds
            .AsNoTracking()
            .OrderBy(outbound => outbound.Position)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns one outbound, or null when the panel holds none under the number.
    /// </summary>
    public async Task<OutboundConfig?> FindAsync(long id, CancellationToken ct)
    {
        var found = await _db.Outbounds
            .AsNoTracking()
            .FirstOrDefaultAsync(outbound => outbound.Id == id, ct)
            .ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Adds the outbound that leaves through the uplink of the host, once, to a fresh database.
    /// </summary>
    public async Task SeedAsync(CancellationToken ct)
    {
        if (await _db.Outbounds.AnyAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        var now = _time.GetUtcNow();
        var entity = new OutboundEntity { CreatedUtc = now, UpdatedUtc = now };
        Write(entity, OutboundDefaults.Direct());

        _db.Outbounds.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds an outbound, giving it the first free mark and the place at the end.
    /// </summary>
    public async Task<OutboundResult> AddAsync(OutboundConfig draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await TakenAsync(draft.Name, 0, ct).ConfigureAwait(false))
        {
            return Taken(draft.Name);
        }

        var mark = await FreeMarkAsync(ct).ConfigureAwait(false);
        if (mark == 0)
        {
            return OutboundResult.No(OutboundOutcome.NoMark, "no-mark", "the panel holds as many outbounds as it can");
        }

        var full = draft with
        {
            Mark = mark,
            Table = OutboundRules.TableOf(mark, draft.Kind),
            Position = await NextPositionAsync(ct).ConfigureAwait(false),
        };

        if (OutboundRules.Check(full) is { } fault)
        {
            return OutboundResult.No(OutboundOutcome.Invalid, fault.Code, fault.Message);
        }

        var now = _time.GetUtcNow();
        var entity = new OutboundEntity { CreatedUtc = now, UpdatedUtc = now };
        Write(entity, full);

        _db.Outbounds.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return OutboundResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the settings of an outbound, keeping its mark and its place.
    /// </summary>
    public async Task<OutboundResult> ChangeAsync(long id, OutboundConfig draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var entity = await _db.Outbounds.FirstOrDefaultAsync(outbound => outbound.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        if (await TakenAsync(draft.Name, id, ct).ConfigureAwait(false))
        {
            return Taken(draft.Name);
        }

        var mark = (uint)entity.Mark;
        var full = draft with { Mark = mark, Table = OutboundRules.TableOf(mark, draft.Kind), Position = entity.Position };
        if (OutboundRules.Check(full) is { } fault)
        {
            return OutboundResult.No(OutboundOutcome.Invalid, fault.Code, fault.Message);
        }

        Write(entity, full);
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return OutboundResult.Done(Read(entity));
    }

    /// <summary>
    /// Turns an outbound on or off.
    /// </summary>
    public async Task<OutboundResult> SwitchAsync(long id, bool on, CancellationToken ct)
    {
        var entity = await _db.Outbounds.FirstOrDefaultAsync(outbound => outbound.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        entity.IsEnabled = on;
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return OutboundResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes an outbound.
    /// </summary>
    public async Task<OutboundResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.Outbounds.FirstOrDefaultAsync(outbound => outbound.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var gone = Read(entity);
        _db.Outbounds.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return OutboundResult.Done(gone);
    }

    /// <summary>
    /// Swaps an outbound with the one above or below it.
    /// </summary>
    public async Task<OutboundResult> MoveAsync(long id, bool up, CancellationToken ct)
    {
        var entity = await _db.Outbounds.FirstOrDefaultAsync(outbound => outbound.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var neighbour = up
            ? await _db.Outbounds.Where(one => one.Position < entity.Position)
                .OrderByDescending(one => one.Position)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
            : await _db.Outbounds.Where(one => one.Position > entity.Position)
                .OrderBy(one => one.Position)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

        if (neighbour is null)
        {
            return OutboundResult.Done(Read(entity));
        }

        (entity.Position, neighbour.Position) = (neighbour.Position, entity.Position);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return OutboundResult.Done(Read(entity));
    }

    private async Task<bool> TakenAsync(string name, long id, CancellationToken ct)
    {
        var body = name.Trim();
        if (await _db.Outbounds.AnyAsync(one => one.Name == body && one.Id != id, ct).ConfigureAwait(false))
        {
            return true;
        }

        return await _db.Balancers.AnyAsync(one => one.Name == body, ct).ConfigureAwait(false);
    }

    private async Task<uint> FreeMarkAsync(CancellationToken ct)
    {
        var taken = await _db.Outbounds.Select(outbound => outbound.Mark).ToListAsync(ct).ConfigureAwait(false);
        for (var mark = OutboundRules.FirstMark; mark <= OutboundRules.LastMark; mark++)
        {
            if (!taken.Contains(mark))
            {
                return mark;
            }
        }

        return 0;
    }

    private async Task<int> NextPositionAsync(CancellationToken ct)
    {
        var last = await _db.Outbounds.MaxAsync(outbound => (int?)outbound.Position, ct).ConfigureAwait(false);

        return (last ?? 0) + 1;
    }

    private static OutboundResult Missing(long id) =>
        OutboundResult.No(OutboundOutcome.Unknown, "unknown-outbound", $"there is no outbound under the number {id}");

    private static OutboundResult Taken(string name) =>
        OutboundResult.No(
            OutboundOutcome.NameTaken,
            "name-taken",
            $"the panel already carries an outbound called '{name}'");

    private static OutboundConfig Read(OutboundEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Kind = entity.Kind,
        Position = entity.Position,
        IsEnabled = entity.IsEnabled,
        Host = entity.Host,
        Port = entity.Port,
        Proxy = entity.Proxy,
        PrivateKey = entity.PrivateKey,
        PublicKey = entity.PublicKey,
        PeerKey = entity.PeerKey,
        PresharedKey = entity.PresharedKey,
        Address = Parts(entity.Address),
        Dns = Parts(entity.Dns),
        Mtu = entity.Mtu,
        Keepalive = entity.Keepalive,
        Mark = (uint)entity.Mark,
        Table = entity.Table,
        CreatedUtc = entity.CreatedUtc,
        UpdatedUtc = entity.UpdatedUtc,
        Obfuscation = new ObfuscationSettings
        {
            Jc = entity.Jc,
            Jmin = entity.Jmin,
            Jmax = entity.Jmax,
            S1 = entity.S1,
            S2 = entity.S2,
            S3 = entity.S3,
            S4 = entity.S4,
            H1 = entity.H1,
            H2 = entity.H2,
            H3 = entity.H3,
            H4 = entity.H4,
            I1 = entity.I1,
            I2 = entity.I2,
            I3 = entity.I3,
            I4 = entity.I4,
            I5 = entity.I5,
            HeaderProtectionKey = entity.HeaderProtectionKey,
            ContentPaddingAddition = entity.ContentPaddingAddition,
            RekeyAfterTime = entity.RekeyAfterTime,
            RekeyTimeout = entity.RekeyTimeout,
            RejectAfterTime = entity.RejectAfterTime,
            KeepaliveTimeout = entity.KeepaliveTimeout,
            MaxHandshakeAttempts = entity.MaxHandshakeAttempts,
            RandomTrailers = entity.RandomTrailers,
            DisableCookies = entity.DisableCookies,
        },
    };

    private static void Write(OutboundEntity entity, OutboundConfig outbound)
    {
        entity.Name = outbound.Name;
        entity.Kind = outbound.Kind;
        entity.Position = outbound.Position;
        entity.IsEnabled = outbound.IsEnabled;
        entity.Host = outbound.Host.Trim();
        entity.Port = outbound.Port;
        entity.Proxy = outbound.Proxy.Trim();
        entity.PrivateKey = outbound.PrivateKey;
        entity.PublicKey = outbound.PrivateKey.Length > 0 ? Curve25519.PublicOf(outbound.PrivateKey) : string.Empty;
        entity.PeerKey = outbound.PeerKey.Trim();
        entity.PresharedKey = outbound.PresharedKey.Trim();
        entity.Address = string.Join(", ", outbound.Address);
        entity.Dns = string.Join(", ", outbound.Dns);
        entity.Mtu = outbound.Mtu;
        entity.Keepalive = outbound.Keepalive;
        entity.Mark = outbound.Mark;
        entity.Table = outbound.Table;
        entity.Jc = outbound.Obfuscation.Jc;
        entity.Jmin = outbound.Obfuscation.Jmin;
        entity.Jmax = outbound.Obfuscation.Jmax;
        entity.S1 = outbound.Obfuscation.S1;
        entity.S2 = outbound.Obfuscation.S2;
        entity.S3 = outbound.Obfuscation.S3;
        entity.S4 = outbound.Obfuscation.S4;
        entity.H1 = outbound.Obfuscation.H1;
        entity.H2 = outbound.Obfuscation.H2;
        entity.H3 = outbound.Obfuscation.H3;
        entity.H4 = outbound.Obfuscation.H4;
        entity.I1 = Text(outbound.Obfuscation.I1);
        entity.I2 = Text(outbound.Obfuscation.I2);
        entity.I3 = Text(outbound.Obfuscation.I3);
        entity.I4 = Text(outbound.Obfuscation.I4);
        entity.I5 = Text(outbound.Obfuscation.I5);
        entity.HeaderProtectionKey = outbound.Obfuscation.HeaderProtectionKey.Trim();
        entity.ContentPaddingAddition = outbound.Obfuscation.ContentPaddingAddition.Trim();
        entity.RekeyAfterTime = outbound.Obfuscation.RekeyAfterTime.Trim();
        entity.RekeyTimeout = outbound.Obfuscation.RekeyTimeout.Trim();
        entity.RejectAfterTime = outbound.Obfuscation.RejectAfterTime.Trim();
        entity.KeepaliveTimeout = outbound.Obfuscation.KeepaliveTimeout.Trim();
        entity.MaxHandshakeAttempts = outbound.Obfuscation.MaxHandshakeAttempts.Trim();
        entity.RandomTrailers = outbound.Obfuscation.RandomTrailers;
        entity.DisableCookies = outbound.Obfuscation.DisableCookies;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
