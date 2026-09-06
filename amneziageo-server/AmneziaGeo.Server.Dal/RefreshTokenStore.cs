using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Auth;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps refresh tokens in the database, rotating them and ending a session whose token was replayed.
/// </summary>
public sealed class RefreshTokenStore : IRefreshTokens
{
    private const int SecretLength = 32;

    private readonly AppDbContext _db;

    private readonly AccessResolver _access;

    private readonly AuthOptions _options;

    /// <summary>
    /// ctor
    /// </summary>
    public RefreshTokenStore(AppDbContext db, AccessResolver access, AuthOptions options)
    {
        _db = db;
        _access = access;
        _options = options;
    }

    /// <summary>
    /// Opens a session and returns its first refresh token.
    /// </summary>
    public async Task<(long SessionId, string Refresh)> OpenAsync(
        long principalId,
        AuthScheme scheme,
        string? address,
        string? agent,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var session = new SessionEntity
        {
            UserId = principalId,
            Scheme = scheme,
            CreatedUtc = now,
            AbsoluteEndUtc = now.Add(_options.SessionLifetime),
            Address = address,
            Agent = agent,
        };

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var minted = Mint(session.Id, now, session.AbsoluteEndUtc);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return (session.Id, minted.Secret);
    }

    /// <summary>
    /// Trades a refresh token for the next one, extending the session up to its ceiling.
    /// </summary>
    public async Task<RefreshResult> RotateAsync(string refresh, DateTimeOffset now, CancellationToken ct)
    {
        var hash = Fingerprint(refresh);
        var token = await _db.RefreshTokens
            .Include(item => item.Session)
            .FirstOrDefaultAsync(item => item.TokenHash == hash, ct)
            .ConfigureAwait(false);

        if (token?.Session is not { } session)
        {
            return new RefreshResult(RefreshOutcome.Unknown, null, null);
        }

        if (session.EndedUtc is not null)
        {
            return new RefreshResult(RefreshOutcome.SessionEnded, null, null);
        }

        if (session.AbsoluteEndUtc <= now)
        {
            await EndAsync(session.Id, now, ct).ConfigureAwait(false);

            return new RefreshResult(RefreshOutcome.SessionEnded, null, null);
        }

        if (token.UsedUtc is not null && (token.GraceEndUtc is null || token.GraceEndUtc < now))
        {
            await EndAsync(session.Id, now, ct).ConfigureAwait(false);

            return new RefreshResult(RefreshOutcome.Replayed, null, null);
        }

        if (token.UsedUtc is null && token.ExpiresUtc <= now)
        {
            return new RefreshResult(RefreshOutcome.Expired, null, null);
        }

        var principal = await PrincipalAsync(session, ct).ConfigureAwait(false);
        if (principal is null)
        {
            await EndAsync(session.Id, now, ct).ConfigureAwait(false);

            return new RefreshResult(RefreshOutcome.SessionEnded, null, null);
        }

        var minted = Mint(session.Id, now, session.AbsoluteEndUtc);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        token.UsedUtc ??= now;
        token.GraceEndUtc = now.Add(_options.RotationGrace);
        token.ReplacedBy = minted.Entity.Id;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new RefreshResult(RefreshOutcome.Issued, minted.Secret, principal);
    }

    /// <summary>
    /// Ends a session and every token in it.
    /// </summary>
    public async Task EndAsync(long sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(item => item.Id == sessionId, ct).ConfigureAwait(false);
        if (session is null || session.EndedUtc is not null)
        {
            return;
        }

        session.EndedUtc = now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the hash a token is stored under.
    /// </summary>
    public static byte[] Fingerprint(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private async Task<Principal?> PrincipalAsync(SessionEntity session, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(item => item.Id == session.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsEnabled)
        {
            return null;
        }

        var scopes = (await _access.ScopesAsync(user).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        if (session.Scheme == AuthScheme.Password)
        {
            scopes.Add(Scopes.ChangePassword);
        }

        return new Principal(user.Id, user.Name, session.Scheme, session.Id, scopes);
    }

    private (RefreshTokenEntity Entity, string Secret) Mint(long sessionId, DateTimeOffset now, DateTimeOffset ceiling)
    {
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretLength));
        var expires = now.Add(_options.RefreshLifetime);
        var entity = new RefreshTokenEntity
        {
            SessionId = sessionId,
            TokenHash = Fingerprint(secret),
            IssuedUtc = now,
            ExpiresUtc = expires < ceiling ? expires : ceiling,
        };

        _db.RefreshTokens.Add(entity);

        return (entity, secret);
    }
}
