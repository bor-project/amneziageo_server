using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps who accepted the terms of use of the panel and when.
/// </summary>
public sealed class TermsStore
{
    private const int LoginLength = 256;

    private readonly AppDbContext _db;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public TermsStore(AppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the latest acceptance, or null before the first one.
    /// </summary>
    public async Task<TermsAcceptance?> LatestAsync(CancellationToken ct)
    {
        var held = await _db.Terms.AsNoTracking().OrderByDescending(row => row.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return held is null ? null : new TermsAcceptance(held.Version, held.Login, held.AcceptedUtc);
    }

    /// <summary>
    /// Writes an acceptance of a version by an account.
    /// </summary>
    public async Task<TermsAcceptance> AcceptAsync(int version, long? userId, string login, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(login);

        var row = new TermsEntity
        {
            Version = version,
            UserId = userId,
            Login = login.Length > LoginLength ? login[..LoginLength] : login,
            AcceptedUtc = _time.GetUtcNow(),
        };
        _db.Terms.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new TermsAcceptance(row.Version, row.Login, row.AcceptedUtc);
    }
}

/// <summary>
/// One acceptance of the terms of use: the version, the account and the instant.
/// </summary>
public sealed record TermsAcceptance(int Version, string Login, DateTimeOffset AcceptedUtc);
