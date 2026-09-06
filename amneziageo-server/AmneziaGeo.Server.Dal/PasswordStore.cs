using AmneziaGeo.Server.Auth;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps the passwords of accounts in the database.
/// </summary>
public sealed class PasswordStore : IPasswords
{
    private readonly Db _db;

    private readonly AuthOptions _options;

    /// <summary>
    /// ctor
    /// </summary>
    public PasswordStore(Db db, AuthOptions options)
    {
        _db = db;
        _options = options;
    }

    /// <summary>
    /// Sets the password of an account and clears its failure count.
    /// </summary>
    public async Task SetAsync(long principalId, string password, bool mustChange, DateTimeOffset now, CancellationToken ct)
    {
        var stored = PasswordHasher.Hash(password);

        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var command = Sql.Command(
            connection,
            """
            INSERT INTO password_credential
                (principal_id, algorithm, iterations, salt, hash, changed_utc, must_change, failed_count, locked_until_utc)
            VALUES (@id, @algorithm, @iterations, @salt, @hash, @now, @must, 0, NULL)
            ON CONFLICT (principal_id) DO UPDATE SET
                algorithm = excluded.algorithm,
                iterations = excluded.iterations,
                salt = excluded.salt,
                hash = excluded.hash,
                changed_utc = excluded.changed_utc,
                must_change = excluded.must_change,
                failed_count = 0,
                locked_until_utc = NULL;
            """,
            ("@id", principalId),
            ("@algorithm", stored.Algorithm),
            ("@iterations", stored.Iterations),
            ("@salt", stored.Salt),
            ("@hash", stored.Hash),
            ("@now", Sql.Text(now)),
            ("@must", mustChange ? 1 : 0));

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks a password, counting failures and locking the account past the allowed number.
    /// </summary>
    public async Task<PasswordCheck> CheckAsync(long principalId, string password, DateTimeOffset now, CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        var found = default(Entry);

        using (var command = Sql.Command(
            connection,
            """
            SELECT algorithm, iterations, salt, hash, must_change, failed_count, locked_until_utc
            FROM password_credential WHERE principal_id = @id;
            """,
            ("@id", principalId)))
        using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                found = new Entry(
                    new StoredPassword(reader.GetString(0), reader.GetInt32(1), (byte[])reader[2], (byte[])reader[3]),
                    reader.GetInt64(4) != 0,
                    reader.GetInt32(5),
                    Sql.Time(reader.TextOrNull(6)));
            }
        }

        if (found is null)
        {
            return PasswordCheck.NotSet;
        }

        if (found.LockedUntil is { } until && until > now)
        {
            return PasswordCheck.Locked;
        }

        if (!PasswordHasher.Verify(found.Stored, password))
        {
            return await FailAsync(connection, principalId, found.Failed + 1, now, ct).ConfigureAwait(false);
        }

        using (var command = Sql.Command(
            connection,
            "UPDATE password_credential SET failed_count = 0, locked_until_utc = NULL WHERE principal_id = @id;",
            ("@id", principalId)))
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return found.MustChange ? PasswordCheck.MustChange : PasswordCheck.Ok;
    }

    /// <summary>
    /// Tells whether an account carries a password.
    /// </summary>
    public async Task<bool> HasAsync(long principalId, CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var command = Sql.Command(
            connection,
            "SELECT EXISTS(SELECT 1 FROM password_credential WHERE principal_id = @id);",
            ("@id", principalId));

        return Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 1;
    }

    /// <summary>
    /// Drops the password of an account.
    /// </summary>
    public async Task ClearAsync(long principalId, CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var command = Sql.Command(
            connection,
            "DELETE FROM password_credential WHERE principal_id = @id;",
            ("@id", principalId));

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<PasswordCheck> FailAsync(
        SqliteConnection connection,
        long principalId,
        int failed,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var locked = failed >= _options.FailedAttempts;

        using var command = Sql.Command(
            connection,
            "UPDATE password_credential SET failed_count = @failed, locked_until_utc = @until WHERE principal_id = @id;",
            ("@failed", locked ? 0 : failed),
            ("@until", locked ? Sql.Text(now.Add(_options.LockDuration)) : null),
            ("@id", principalId));

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        return locked ? PasswordCheck.Locked : PasswordCheck.Wrong;
    }

    private sealed record Entry(StoredPassword Stored, bool MustChange, int Failed, DateTimeOffset? LockedUntil);
}
