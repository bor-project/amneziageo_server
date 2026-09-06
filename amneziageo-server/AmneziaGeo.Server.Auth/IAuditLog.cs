namespace AmneziaGeo.Server.Auth;

/// <summary>
/// Writes what was done and by whom.
/// </summary>
public interface IAuditLog
{
    /// <summary>
    /// Records one action.
    /// </summary>
    Task WriteAsync(
        DateTimeOffset at,
        long? principalId,
        AuthScheme scheme,
        string action,
        string? target,
        string? detail,
        string? address,
        CancellationToken ct);
}
