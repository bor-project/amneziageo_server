using AmneziaGeo.Server.Core.Panel;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Reads and saves the settings of the subscriptions.
/// </summary>
public sealed class SubscriptionStore
{
    private const long Row = 1;

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public SubscriptionStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the settings the subscriptions are served with, the ones they start with when there are none.
    /// </summary>
    public async Task<SubscriptionSettings> ReadAsync(CancellationToken ct)
    {
        var held = await _db.Subscription.AsNoTracking().FirstOrDefaultAsync(row => row.Id == Row, ct)
            .ConfigureAwait(false);

        return held is null ? SubscriptionDefaults.Settings : Read(held);
    }

    /// <summary>
    /// Saves the settings of the subscriptions.
    /// </summary>
    public async Task<SubscriptionSettings> SaveAsync(SubscriptionSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var held = await _db.Subscription.FirstOrDefaultAsync(row => row.Id == Row, ct).ConfigureAwait(false);
        if (held is null)
        {
            held = new SubscriptionEntity { Id = Row };
            _db.Add(held);
        }

        Write(held, settings);
        held.UpdatedUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Read(held);
    }

    private static SubscriptionSettings Read(SubscriptionEntity row) => new()
    {
        IsEnabled = row.IsEnabled,
        Listen = PanelList.Split(row.Listen),
        Domains = PanelList.Split(row.Domains),
        Port = row.Port,
        Opened = row.Opened,
        Path = row.Path,
        Certificate = row.Certificate,
        CertificateKey = row.CertificateKey,
        UpdateHours = row.UpdateHours,
        Title = row.Title,
    };

    private static void Write(SubscriptionEntity row, SubscriptionSettings settings)
    {
        row.IsEnabled = settings.IsEnabled;
        row.Listen = PanelList.Line(settings.Listen);
        row.Domains = PanelList.Line(settings.Domains);
        row.Port = settings.Port;
        row.Opened = settings.Opened;
        row.Path = settings.Path;
        row.Certificate = settings.Certificate;
        row.CertificateKey = settings.CertificateKey;
        row.UpdateHours = settings.UpdateHours;
        row.Title = settings.Title;
    }
}
