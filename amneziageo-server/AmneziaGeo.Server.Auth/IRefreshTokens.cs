namespace AmneziaGeo.Server.Auth;

/// <summary>
/// What a refresh attempt produced.
/// </summary>
public enum RefreshOutcome
{
    Issued = 0,
    Unknown = 1,
    Expired = 2,
    Replayed = 3,
    SessionEnded = 4,
}

/// <summary>
/// The pair handed back after a successful refresh.
/// </summary>
public sealed record RefreshResult(RefreshOutcome Outcome, string? Refresh, Principal? Principal);

/// <summary>
/// Keeps refresh tokens, rotating them and ending a session whose token was replayed.
/// </summary>
public interface IRefreshTokens
{
    /// <summary>
    /// Opens a session and returns its first refresh token.
    /// </summary>
    Task<(long SessionId, string Refresh)> OpenAsync(
        long principalId,
        AuthScheme scheme,
        string? address,
        string? agent,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>
    /// Trades a refresh token for the next one, extending the session up to its ceiling.
    /// </summary>
    Task<RefreshResult> RotateAsync(string refresh, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Ends a session and every token in it.
    /// </summary>
    Task EndAsync(long sessionId, DateTimeOffset now, CancellationToken ct);
}
