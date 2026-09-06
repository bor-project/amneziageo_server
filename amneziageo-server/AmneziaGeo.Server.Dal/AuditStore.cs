using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Writes the audit trail into the database.
/// </summary>
public sealed class AuditStore : IAuditLog
{
    private readonly Db _db;

    /// <summary>
    /// ctor
    /// </summary>
    public AuditStore(Db db)
    {
        _db = db;
    }

    /// <summary>
    /// Records one action.
    /// </summary>
    public async Task WriteAsync(
        DateTimeOffset at,
        long? principalId,
        AuthScheme scheme,
        string action,
        string? target,
        string? detail,
        string? address,
        CancellationToken ct)
    {
        using var connection = await _db.OpenAsync(ct).ConfigureAwait(false);
        using var command = Sql.Command(
            connection,
            "INSERT INTO audit (at_utc, principal_id, scheme, action, target, detail, address) VALUES (@at, @principal, @scheme, @action, @target, @detail, @address);",
            ("@at", Sql.Text(at)),
            ("@principal", principalId),
            ("@scheme", scheme.ToString()),
            ("@action", action),
            ("@target", target),
            ("@detail", detail),
            ("@address", address));

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
