using AmneziaGeo.Server.Auth;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps the accounts of the panel in the database.
/// </summary>
public sealed class PrincipalStore : IPrincipals
{
    private const string Columns = "p.id, p.name, p.display_name, p.kind, p.role, p.is_enabled";

    private static readonly IReadOnlySet<string> _empty = new HashSet<string>(StringComparer.Ordinal);

    private readonly Db _db;

    /// <summary>
    /// ctor
    /// </summary>
    public PrincipalStore(Db db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns every account, by name.
    /// </summary>
    public async Task<IReadOnlyList<PrincipalRecord>> ListAsync(CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        var found = new List<PrincipalRecord>();

        using (var command = Sql.Command(connection, $"SELECT {Columns} FROM principal p ORDER BY p.name;"))
        using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                found.Add(Read(reader));
            }
        }

        var extra = await ExtraAsync(connection, ct).ConfigureAwait(false);

        return [.. found.Select(item => item with { Extra = Scopes(extra, item.Id) })];
    }

    /// <summary>
    /// Returns an account by name, or null when there is none.
    /// </summary>
    public async Task<PrincipalRecord?> FindAsync(string name, CancellationToken ct) =>
        await OneAsync($"SELECT {Columns} FROM principal p WHERE p.name = @name;", ("@name", name), ct).ConfigureAwait(false);

    /// <summary>
    /// Returns an account by identifier, or null when there is none.
    /// </summary>
    public async Task<PrincipalRecord?> FindAsync(long id, CancellationToken ct) =>
        await OneAsync($"SELECT {Columns} FROM principal p WHERE p.id = @id;", ("@id", id), ct).ConfigureAwait(false);

    /// <summary>
    /// Returns the account a host user is registered as, or null when it is not registered.
    /// </summary>
    public async Task<PrincipalRecord?> FindByHostUserAsync(string userName, CancellationToken ct) =>
        await OneAsync(
            $"SELECT {Columns} FROM principal p JOIN host_identity h ON h.principal_id = p.id WHERE h.user_name = @user;",
            ("@user", userName),
            ct).ConfigureAwait(false);

    /// <summary>
    /// Tells whether an enabled administrator already exists.
    /// </summary>
    public async Task<bool> HasAdminAsync(CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var command = Sql.Command(
            connection,
            "SELECT EXISTS(SELECT 1 FROM principal WHERE role = 'admin' AND is_enabled = 1);");

        return Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 1;
    }

    /// <summary>
    /// Adds an account of the panel.
    /// </summary>
    public async Task<PrincipalRecord> AddAsync(string name, string displayName, Role role, CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var command = Sql.Command(
            connection,
            """
            INSERT INTO principal (name, display_name, kind, role, is_enabled, created_utc)
            VALUES (@name, @display, 'local', @role, 1, @now)
            RETURNING id;
            """,
            ("@name", name),
            ("@display", displayName),
            ("@role", Roles.Text(role)),
            ("@now", Sql.Text(DateTimeOffset.UtcNow)));

        var id = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));

        return new PrincipalRecord(id, name, displayName, PrincipalKind.Local, role, true, _empty);
    }

    /// <summary>
    /// Registers a host user as an account and returns it.
    /// </summary>
    public async Task<PrincipalRecord> RegisterHostUserAsync(string userName, uint uid, string displayName, Role role, CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var now = Sql.Text(DateTimeOffset.UtcNow);
        var id = 0L;

        using (var command = Sql.Command(
            connection,
            """
            INSERT INTO principal (name, display_name, kind, role, is_enabled, created_utc)
            VALUES (@name, @display, 'host', @role, 1, @now)
            RETURNING id;
            """,
            ("@name", userName),
            ("@display", displayName),
            ("@role", Roles.Text(role)),
            ("@now", now)))
        {
            command.Transaction = transaction;
            id = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        using (var command = Sql.Command(
            connection,
            """
            INSERT INTO host_identity (principal_id, user_name, uid, registered_utc, seen_utc)
            VALUES (@id, @user, @uid, @now, @now);
            """,
            ("@id", id),
            ("@user", userName),
            ("@uid", (long)uid),
            ("@now", now)))
        {
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new PrincipalRecord(id, userName, displayName, PrincipalKind.Host, role, true, _empty);
    }

    /// <summary>
    /// Records that a host user was seen, keeping its user id current.
    /// </summary>
    public async Task SeenAsync(long id, uint uid, DateTimeOffset now, CancellationToken ct) =>
        await RunAsync(
            "UPDATE host_identity SET uid = @uid, seen_utc = @now WHERE principal_id = @id;",
            ct,
            ("@uid", (long)uid),
            ("@now", Sql.Text(now)),
            ("@id", id)).ConfigureAwait(false);

    /// <summary>
    /// Sets the role of an account.
    /// </summary>
    public async Task SetRoleAsync(long id, Role role, CancellationToken ct) =>
        await RunAsync(
            "UPDATE principal SET role = @role WHERE id = @id;",
            ct,
            ("@role", Roles.Text(role)),
            ("@id", id)).ConfigureAwait(false);

    /// <summary>
    /// Replaces the rights granted on top of the role.
    /// </summary>
    public async Task SetExtraAsync(long id, IEnumerable<string> scopes, CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        using (var command = Sql.Command(connection, "DELETE FROM principal_scope WHERE principal_id = @id;", ("@id", id)))
        {
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var scope in scopes.Distinct(StringComparer.Ordinal))
        {
            using var command = Sql.Command(
                connection,
                "INSERT INTO principal_scope (principal_id, scope) VALUES (@id, @scope);",
                ("@id", id),
                ("@scope", scope));

            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns an account on or off.
    /// </summary>
    public async Task SetEnabledAsync(long id, bool enabled, DateTimeOffset now, CancellationToken ct) =>
        await RunAsync(
            "UPDATE principal SET is_enabled = @enabled, disabled_utc = @disabled WHERE id = @id;",
            ct,
            ("@enabled", enabled ? 1 : 0),
            ("@disabled", enabled ? null : Sql.Text(now)),
            ("@id", id)).ConfigureAwait(false);

    /// <summary>
    /// Removes an account with everything hanging off it.
    /// </summary>
    public async Task RemoveAsync(long id, CancellationToken ct) =>
        await RunAsync("DELETE FROM principal WHERE id = @id;", ct, ("@id", id)).ConfigureAwait(false);

    private async Task<PrincipalRecord?> OneAsync(string text, (string Name, object? Value) parameter, CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        var found = default(PrincipalRecord);

        using (var command = Sql.Command(connection, text, parameter))
        using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                found = Read(reader);
            }
        }

        if (found is null)
        {
            return null;
        }

        var extra = await ExtraAsync(connection, ct, found.Id).ConfigureAwait(false);

        return found with { Extra = Scopes(extra, found.Id) };
    }

    private async Task RunAsync(string text, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var command = Sql.Command(connection, text, parameters);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<List<(long Id, string Scope)>> ExtraAsync(SqliteConnection connection, CancellationToken ct, long? id = null)
    {
        var text = id is null
            ? "SELECT principal_id, scope FROM principal_scope;"
            : "SELECT principal_id, scope FROM principal_scope WHERE principal_id = @id;";

        using var command = id is null
            ? Sql.Command(connection, text)
            : Sql.Command(connection, text, ("@id", id.Value));

        using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var found = new List<(long, string)>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            found.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        return found;
    }

    private static IReadOnlySet<string> Scopes(List<(long Id, string Scope)> extra, long id) =>
        extra.Where(item => item.Id == id).Select(item => item.Scope).ToHashSet(StringComparer.Ordinal);

    private static PrincipalRecord Read(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3) == "host" ? PrincipalKind.Host : PrincipalKind.Local,
        Roles.Parse(reader.GetString(4)),
        reader.GetInt64(5) != 0,
        _empty);
}
