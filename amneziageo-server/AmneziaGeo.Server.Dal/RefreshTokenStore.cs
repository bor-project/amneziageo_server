using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Auth;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps refresh tokens in the database, rotating them and ending a session whose token was replayed.
/// </summary>
public sealed class RefreshTokenStore : IRefreshTokens
{
    private const int SecretLength = 32;

    private readonly Db _db;

    private readonly IPrincipals _principals;

    private readonly AuthOptions _options;

    /// <summary>
    /// ctor
    /// </summary>
    public RefreshTokenStore(Db db, IPrincipals principals, AuthOptions options)
    {
        _db = db;
        _principals = principals;
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
        var ceiling = now.Add(_options.SessionLifetime);

        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var sessionId = 0L;

        using (var command = Sql.Command(
            connection,
            """
            INSERT INTO session (principal_id, scheme, created_utc, absolute_end_utc, address, agent)
            VALUES (@principal, @scheme, @now, @ceiling, @address, @agent)
            RETURNING id;
            """,
            ("@principal", principalId),
            ("@scheme", scheme.ToString()),
            ("@now", Sql.Text(now)),
            ("@ceiling", Sql.Text(ceiling)),
            ("@address", address),
            ("@agent", agent)))
        {
            command.Transaction = transaction;
            sessionId = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        var minted = await MintAsync(connection, (SqliteTransaction)transaction, sessionId, now, ceiling, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return (sessionId, minted.Secret);
    }

    /// <summary>
    /// Trades a refresh token for the next one, extending the session up to its ceiling.
    /// </summary>
    public async Task<RefreshResult> RotateAsync(string refresh, DateTimeOffset now, CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        var found = await FindAsync(connection, refresh, ct).ConfigureAwait(false);
        if (found is null)
        {
            return new RefreshResult(RefreshOutcome.Unknown, null, null);
        }

        if (found.SessionEnded is not null)
        {
            return new RefreshResult(RefreshOutcome.SessionEnded, null, null);
        }

        if (found.Ceiling <= now)
        {
            await EndAsync(found.SessionId, now, ct).ConfigureAwait(false);

            return new RefreshResult(RefreshOutcome.SessionEnded, null, null);
        }

        if (found.Used is not null && (found.GraceEnd is null || found.GraceEnd < now))
        {
            await EndAsync(found.SessionId, now, ct).ConfigureAwait(false);

            return new RefreshResult(RefreshOutcome.Replayed, null, null);
        }

        if (found.Used is null && found.Expires <= now)
        {
            return new RefreshResult(RefreshOutcome.Expired, null, null);
        }

        var principal = await PrincipalAsync(found, ct).ConfigureAwait(false);
        if (principal is null)
        {
            await EndAsync(found.SessionId, now, ct).ConfigureAwait(false);

            return new RefreshResult(RefreshOutcome.SessionEnded, null, null);
        }

        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var minted = await MintAsync(connection, (SqliteTransaction)transaction, found.SessionId, now, found.Ceiling, ct).ConfigureAwait(false);

        using (var command = Sql.Command(
            connection,
            """
            UPDATE refresh_token
            SET used_utc = COALESCE(used_utc, @now), grace_end_utc = @grace, replaced_by = @next
            WHERE id = @id;
            """,
            ("@now", Sql.Text(now)),
            ("@grace", Sql.Text(now.Add(_options.RotationGrace))),
            ("@next", minted.Id),
            ("@id", found.TokenId)))
        {
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new RefreshResult(RefreshOutcome.Issued, minted.Secret, principal);
    }

    /// <summary>
    /// Ends a session and every token in it.
    /// </summary>
    public async Task EndAsync(long sessionId, DateTimeOffset now, CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var command = Sql.Command(
            connection,
            "UPDATE session SET ended_utc = COALESCE(ended_utc, @now) WHERE id = @id;",
            ("@now", Sql.Text(now)),
            ("@id", sessionId));

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the hash a token is stored under.
    /// </summary>
    public static byte[] Fingerprint(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private async Task<Principal?> PrincipalAsync(Entry entry, CancellationToken ct)
    {
        var record = await _principals.FindAsync(entry.PrincipalId, ct).ConfigureAwait(false);
        if (record is null || !record.IsEnabled)
        {
            return null;
        }

        var scopes = record.Scopes.ToHashSet(StringComparer.Ordinal);
        if (entry.Scheme == AuthScheme.Password)
        {
            scopes.Add(Auth.Scopes.ChangePassword);
        }

        return new Principal(record.Id, record.Name, entry.Scheme, entry.SessionId, scopes);
    }

    private async Task<(long Id, string Secret)> MintAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long sessionId,
        DateTimeOffset now,
        DateTimeOffset ceiling,
        CancellationToken ct)
    {
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretLength));
        var expires = now.Add(_options.RefreshLifetime);

        using var command = Sql.Command(
            connection,
            """
            INSERT INTO refresh_token (session_id, token_hash, issued_utc, expires_utc)
            VALUES (@session, @hash, @now, @expires)
            RETURNING id;
            """,
            ("@session", sessionId),
            ("@hash", Fingerprint(secret)),
            ("@now", Sql.Text(now)),
            ("@expires", Sql.Text(expires < ceiling ? expires : ceiling)));

        command.Transaction = transaction;
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));

        return (id, secret);
    }

    private static async Task<Entry?> FindAsync(SqliteConnection connection, string refresh, CancellationToken ct)
    {
        using var command = Sql.Command(
            connection,
            """
            SELECT r.id, r.session_id, r.expires_utc, r.grace_end_utc, r.used_utc,
                   s.ended_utc, s.absolute_end_utc, s.principal_id, s.scheme
            FROM refresh_token r JOIN session s ON s.id = r.session_id
            WHERE r.token_hash = @hash;
            """,
            ("@hash", Fingerprint(refresh)));

        using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new Entry(
            reader.GetInt64(0),
            reader.GetInt64(1),
            Sql.Time(reader.GetString(2))!.Value,
            Sql.Time(reader.TextOrNull(3)),
            Sql.Time(reader.TextOrNull(4)),
            Sql.Time(reader.TextOrNull(5)),
            Sql.Time(reader.GetString(6))!.Value,
            reader.GetInt64(7),
            Enum.TryParse<AuthScheme>(reader.GetString(8), out var scheme) ? scheme : AuthScheme.None);
    }

    private sealed record Entry(
        long TokenId,
        long SessionId,
        DateTimeOffset Expires,
        DateTimeOffset? GraceEnd,
        DateTimeOffset? Used,
        DateTimeOffset? SessionEnded,
        DateTimeOffset Ceiling,
        long PrincipalId,
        AuthScheme Scheme);
}
