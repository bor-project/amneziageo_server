using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What managing an endpoint produced.
/// </summary>
public enum ConfigOutcome
{
    Ok = 0,
    Invalid = 1,
    NameTaken = 2,
    Unknown = 3,
    PortTaken = 4,
}

/// <summary>
/// The endpoint a command produced, and why it was refused.
/// </summary>
public sealed record ConfigResult(ConfigOutcome Outcome, string Code, string Message, ServerConfig? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == ConfigOutcome.Ok;

    /// <summary>
    /// Returns the endpoint a command produced.
    /// </summary>
    public static ConfigResult Done(ServerConfig config) => new(ConfigOutcome.Ok, string.Empty, string.Empty, config);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static ConfigResult No(ConfigOutcome outcome, string code, string message) => new(outcome, code, message, null);
}

/// <summary>
/// Adds, changes and removes the endpoints the panel holds.
/// </summary>
public sealed class ConfigStore
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public ConfigStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every endpoint the panel holds.
    /// </summary>
    public async Task<IReadOnlyList<ServerConfig>> ListAsync(CancellationToken ct)
    {
        var found = await _db.Configs.AsNoTracking().OrderBy(config => config.Name).ToListAsync(ct).ConfigureAwait(false);

        return [.. found.Select(Read)];
    }

    /// <summary>
    /// Returns one endpoint, or null when the panel holds none under the number.
    /// </summary>
    public async Task<ServerConfig?> FindAsync(long id, CancellationToken ct)
    {
        var found = await _db.Configs.AsNoTracking().FirstOrDefaultAsync(config => config.Id == id, ct).ConfigureAwait(false);

        return found is null ? null : Read(found);
    }

    /// <summary>
    /// Returns the first port from the wanted one up that no endpoint listens on.
    /// </summary>
    public async Task<int> FreePortAsync(int wanted, CancellationToken ct)
    {
        var taken = await _db.Configs.Select(config => config.ListenPort).ToListAsync(ct).ConfigureAwait(false);
        var port = wanted;
        while (port < 65535 && taken.Contains(port))
        {
            port++;
        }

        return port;
    }

    /// <summary>
    /// Adds an endpoint with the settings it carries.
    /// </summary>
    public async Task<ConfigResult> AddAsync(ServerConfig draft, CancellationToken ct)
    {
        if (ConfigRules.Check(draft) is { } fault)
        {
            return ConfigResult.No(ConfigOutcome.Invalid, fault.Code, fault.Message);
        }

        if (await _db.Configs.AnyAsync(config => config.Name == draft.Name, ct).ConfigureAwait(false))
        {
            return ConfigResult.No(
                ConfigOutcome.NameTaken,
                "name-taken",
                $"the panel already carries an endpoint called '{draft.Name}'");
        }

        if (await _db.Configs.AnyAsync(config => config.ListenPort == draft.ListenPort, ct).ConfigureAwait(false))
        {
            return ConfigResult.No(
                ConfigOutcome.PortTaken,
                "port-taken",
                $"the panel already listens on port {draft.ListenPort}");
        }

        var now = _time.GetUtcNow();
        var entity = new ConfigEntity { CreatedUtc = now, UpdatedUtc = now };
        Write(entity, draft);

        _db.Configs.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ConfigResult.Done(Read(entity));
    }

    /// <summary>
    /// Replaces the settings of an endpoint.
    /// </summary>
    public async Task<ConfigResult> ChangeAsync(long id, ServerConfig draft, CancellationToken ct)
    {
        var entity = await _db.Configs.FirstOrDefaultAsync(config => config.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        if (ConfigRules.Check(draft) is { } fault)
        {
            return ConfigResult.No(ConfigOutcome.Invalid, fault.Code, fault.Message);
        }

        if (await _db.Configs.AnyAsync(config => config.Name == draft.Name && config.Id != id, ct).ConfigureAwait(false))
        {
            return ConfigResult.No(
                ConfigOutcome.NameTaken,
                "name-taken",
                $"the panel already carries an endpoint called '{draft.Name}'");
        }

        if (await _db.Configs.AnyAsync(config => config.ListenPort == draft.ListenPort && config.Id != id, ct)
            .ConfigureAwait(false))
        {
            return ConfigResult.No(
                ConfigOutcome.PortTaken,
                "port-taken",
                $"the panel already listens on port {draft.ListenPort}");
        }

        Write(entity, draft);
        entity.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ConfigResult.Done(Read(entity));
    }

    /// <summary>
    /// Removes an endpoint.
    /// </summary>
    public async Task<ConfigResult> RemoveAsync(long id, CancellationToken ct)
    {
        var entity = await _db.Configs.FirstOrDefaultAsync(config => config.Id == id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Missing(id);
        }

        var gone = Read(entity);
        _db.Configs.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ConfigResult.Done(gone);
    }

    private static ConfigResult Missing(long id) =>
        ConfigResult.No(ConfigOutcome.Unknown, "unknown-config", $"there is no endpoint under the number {id}");

    private static ServerConfig Read(ConfigEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Host = entity.Host,
        ListenPort = entity.ListenPort,
        Address = Parts(entity.Address),
        Dns = Parts(entity.Dns),
        AllowedIps = Parts(entity.AllowedIps),
        Mtu = entity.Mtu,
        Keepalive = entity.Keepalive,
        OfflineAfter = entity.OfflineAfter,
        IsEnabled = entity.IsEnabled,
        Nat = entity.Nat,
        Opened = entity.Opened,
        Inbound = (ClientInbound)entity.Inbound,
        Blocked = Parts(entity.Blocked),
        PrivateKey = entity.PrivateKey,
        PublicKey = entity.PublicKey,
        PresharedKey = entity.PresharedKey,
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

    private static void Write(ConfigEntity entity, ServerConfig config)
    {
        entity.Name = config.Name;
        entity.Host = config.Host.Trim();
        entity.ListenPort = config.ListenPort;
        entity.Address = string.Join(", ", config.Address);
        entity.Dns = string.Join(", ", config.Dns);
        entity.AllowedIps = string.Join(", ", config.AllowedIps);
        entity.Mtu = config.Mtu;
        entity.Keepalive = config.Keepalive;
        entity.OfflineAfter = config.OfflineAfter;
        entity.IsEnabled = config.IsEnabled;
        entity.Nat = config.Nat;
        entity.Opened = config.Opened;
        entity.Inbound = (int)config.Inbound;
        entity.Blocked = string.Join(", ", config.Blocked);
        entity.PrivateKey = config.PrivateKey;
        entity.PublicKey = Curve25519.PublicOf(config.PrivateKey);
        entity.PresharedKey = config.PresharedKey.Trim();
        entity.Jc = config.Obfuscation.Jc;
        entity.Jmin = config.Obfuscation.Jmin;
        entity.Jmax = config.Obfuscation.Jmax;
        entity.S1 = config.Obfuscation.S1;
        entity.S2 = config.Obfuscation.S2;
        entity.S3 = config.Obfuscation.S3;
        entity.S4 = config.Obfuscation.S4;
        entity.H1 = config.Obfuscation.H1;
        entity.H2 = config.Obfuscation.H2;
        entity.H3 = config.Obfuscation.H3;
        entity.H4 = config.Obfuscation.H4;
        entity.I1 = Text(config.Obfuscation.I1);
        entity.I2 = Text(config.Obfuscation.I2);
        entity.I3 = Text(config.Obfuscation.I3);
        entity.I4 = Text(config.Obfuscation.I4);
        entity.I5 = Text(config.Obfuscation.I5);
        entity.HeaderProtectionKey = config.Obfuscation.HeaderProtectionKey.Trim();
        entity.ContentPaddingAddition = config.Obfuscation.ContentPaddingAddition.Trim();
        entity.RekeyAfterTime = config.Obfuscation.RekeyAfterTime.Trim();
        entity.RekeyTimeout = config.Obfuscation.RekeyTimeout.Trim();
        entity.RejectAfterTime = config.Obfuscation.RejectAfterTime.Trim();
        entity.KeepaliveTimeout = config.Obfuscation.KeepaliveTimeout.Trim();
        entity.MaxHandshakeAttempts = config.Obfuscation.MaxHandshakeAttempts.Trim();
        entity.RandomTrailers = config.Obfuscation.RandomTrailers;
        entity.DisableCookies = config.Obfuscation.DisableCookies;
    }

    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
