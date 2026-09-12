using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What a command over the proxies produced.
/// </summary>
public enum ProxyOutcome
{
    Ok = 0,
    Invalid = 1,
    NameTaken = 2,
    PortTaken = 3,
    Unknown = 4,
}

/// <summary>
/// The proxy a command produced, and why it was refused.
/// </summary>
public sealed record ProxyResult(ProxyOutcome Outcome, string Code, string Message, ProxyConfig? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == ProxyOutcome.Ok;

    /// <summary>
    /// Returns the proxy a command produced.
    /// </summary>
    public static ProxyResult Done(ProxyConfig proxy) => new(ProxyOutcome.Ok, string.Empty, string.Empty, proxy);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static ProxyResult No(ProxyOutcome outcome, string code, string message) =>
        new(outcome, code, message, null);

    /// <summary>
    /// Returns a refusal the rules produced.
    /// </summary>
    public static ProxyResult No(ProxyFault fault) => new(ProxyOutcome.Invalid, fault.Code, fault.Message, null);
}

/// <summary>
/// Reads and saves the proxies the panel holds.
/// </summary>
public sealed class ProxyStore
{
    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the proxies the panel holds, by name.
    /// </summary>
    public async Task<IReadOnlyList<ProxyConfig>> ListAsync(CancellationToken ct)
    {
        var found = await _db.Set<ProxyEntity>().AsNoTracking().OrderBy(row => row.Name).ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns the proxy the panel holds under a number, null when there is none.
    /// </summary>
    public async Task<ProxyConfig?> FindAsync(long id, CancellationToken ct)
    {
        var held = await _db.Set<ProxyEntity>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, ct)
            .ConfigureAwait(false);

        return held is null ? null : Read(held);
    }

    /// <summary>
    /// Adds a proxy, refusing the one the rules do not allow.
    /// </summary>
    public async Task<ProxyResult> AddAsync(ProxyConfig draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (ProxyRules.Check(draft) is { } broken)
        {
            return ProxyResult.No(broken);
        }

        if (await TakenAsync(draft, 0, ct).ConfigureAwait(false) is { } taken)
        {
            return taken;
        }

        var now = _time.GetUtcNow();
        var row = new ProxyEntity { UpdatedUtc = now };
        Write(row, draft);
        _db.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ProxyResult.Done(Read(row));
    }

    /// <summary>
    /// Changes a proxy, refusing the settings the rules do not allow.
    /// </summary>
    public async Task<ProxyResult> ChangeAsync(long id, ProxyConfig draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var held = await _db.Set<ProxyEntity>().FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Missing(id);
        }

        if (ProxyRules.Check(draft) is { } broken)
        {
            return ProxyResult.No(broken);
        }

        if (await TakenAsync(draft, id, ct).ConfigureAwait(false) is { } taken)
        {
            return taken;
        }

        Write(held, draft);
        held.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ProxyResult.Done(Read(held));
    }

    /// <summary>
    /// Turns a proxy on or off.
    /// </summary>
    public async Task<ProxyResult> SwitchAsync(long id, bool on, CancellationToken ct)
    {
        var held = await _db.Set<ProxyEntity>().FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Missing(id);
        }

        held.IsEnabled = on;
        held.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ProxyResult.Done(Read(held));
    }

    /// <summary>
    /// Removes a proxy.
    /// </summary>
    public async Task<ProxyResult> RemoveAsync(long id, CancellationToken ct)
    {
        var held = await _db.Set<ProxyEntity>().FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Missing(id);
        }

        var gone = Read(held);
        _db.Remove(held);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ProxyResult.Done(gone);
    }

    private async Task<ProxyResult?> TakenAsync(ProxyConfig draft, long id, CancellationToken ct)
    {
        var name = await _db.Set<ProxyEntity>()
            .AnyAsync(row => row.Id != id && row.Name == draft.Name, ct)
            .ConfigureAwait(false);
        if (name)
        {
            return ProxyResult.No(ProxyOutcome.NameTaken, "name-taken", $"'{draft.Name}' is taken by another proxy");
        }

        var port = await _db.Set<ProxyEntity>()
            .AnyAsync(row => row.Id != id && row.Port == draft.Port && row.Kind == draft.Kind, ct)
            .ConfigureAwait(false);

        return port
            ? ProxyResult.No(ProxyOutcome.PortTaken, "port-taken", $"port {draft.Port} is taken by another proxy")
            : null;
    }

    private static ProxyResult Missing(long id) =>
        ProxyResult.No(ProxyOutcome.Unknown, "unknown-proxy", $"there is no proxy under the number {id}");

    private static ProxyConfig Read(ProxyEntity row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Kind = row.Kind,
        IsEnabled = row.IsEnabled,
        Port = row.Port,
        Opened = row.Opened,
        Path = row.Path,
        Target = row.Target,
        Sources = PanelList.Split(row.Sources),
        Certificate = row.Certificate,
        CertificateKey = row.CertificateKey,
    };

    private static void Write(ProxyEntity row, ProxyConfig proxy)
    {
        row.Name = proxy.Name.Trim();
        row.Kind = proxy.Kind;
        row.IsEnabled = proxy.IsEnabled;
        row.Port = proxy.Port;
        row.Opened = proxy.Opened;
        row.Path = proxy.Path.Trim('/');
        row.Target = proxy.Target.Trim();
        row.Sources = PanelList.Line(PanelList.Of(proxy.Sources));
        row.Certificate = proxy.Certificate;
        row.CertificateKey = proxy.CertificateKey;
    }
}
