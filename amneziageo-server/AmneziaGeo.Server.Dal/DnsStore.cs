using AmneziaGeo.Server.Routing.Dns;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What saving the resolver settings produced.
/// </summary>
public enum DnsOutcome
{
    Ok = 0,
    Invalid = 1,
}

/// <summary>
/// The settings a command produced, and why it was refused.
/// </summary>
public sealed record DnsResult(DnsOutcome Outcome, string Code, string Message, DnsSettings? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == DnsOutcome.Ok;

    /// <summary>
    /// Returns the settings a command produced.
    /// </summary>
    public static DnsResult Done(DnsSettings settings) => new(DnsOutcome.Ok, string.Empty, string.Empty, settings);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static DnsResult No(DnsFault fault) => new(DnsOutcome.Invalid, fault.Code, fault.Message, null);
}

/// <summary>
/// Reads and saves the resolver settings the panel holds.
/// </summary>
public sealed class DnsStore
{
    private const long Row = 1;

    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the settings the panel holds, the ones it starts with when there are none.
    /// </summary>
    public async Task<DnsSettings> ReadAsync(CancellationToken ct)
    {
        var held = await _db.Set<DnsSettingsEntity>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == Row, ct)
            .ConfigureAwait(false);

        return held is null ? DnsDefaults.Settings : Read(held);
    }

    /// <summary>
    /// Saves the settings, refusing the ones the rules do not allow.
    /// </summary>
    public async Task<DnsResult> SaveAsync(DnsSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (DnsRules.Check(settings) is { } broken)
        {
            return DnsResult.No(broken);
        }

        var held = await _db.Set<DnsSettingsEntity>().FirstOrDefaultAsync(row => row.Id == Row, ct)
            .ConfigureAwait(false);

        if (held is null)
        {
            held = new DnsSettingsEntity { Id = Row };
            _db.Add(held);
        }

        Write(held, settings);
        held.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return DnsResult.Done(Read(held));
    }

    private static DnsSettings Read(DnsSettingsEntity row) => new()
    {
        IsEnabled = row.IsEnabled,
        Port = row.Port,
        Upstreams = Parts(row.Upstreams),
        Outbound = row.Outbound,
        Listen = Parts(row.Listen),
        NameMinutes = row.NameMinutes,
        CacheSize = row.CacheSize,
        MinTtl = row.MinTtl,
        MaxTtl = row.MaxTtl,
        Intercept = row.Intercept,
        BlockDot = row.BlockDot,
        BlockDoh = row.BlockDoh,
    };

    private static void Write(DnsSettingsEntity row, DnsSettings settings)
    {
        row.IsEnabled = settings.IsEnabled;
        row.Port = settings.Port;
        row.Upstreams = string.Join(",", settings.Upstreams);
        row.Outbound = settings.Outbound;
        row.Listen = string.Join(",", settings.Listen);
        row.NameMinutes = settings.NameMinutes;
        row.CacheSize = settings.CacheSize;
        row.MinTtl = settings.MinTtl;
        row.MaxTtl = settings.MaxTtl;
        row.Intercept = settings.Intercept;
        row.BlockDot = settings.BlockDot;
        row.BlockDoh = settings.BlockDoh;
    }

    private static string[] Parts(string text) =>
        text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
