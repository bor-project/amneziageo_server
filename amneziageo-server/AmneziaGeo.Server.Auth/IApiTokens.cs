namespace AmneziaGeo.Server.Auth;

/// <summary>
/// Long lived tokens for machines.
/// </summary>
public interface IApiTokens
{
    /// <summary>
    /// Mints a token, returning the only copy of its secret.
    /// </summary>
    Task<string> MintAsync(long principalId, string name, DateTimeOffset? expires, CancellationToken ct);

    /// <summary>
    /// Resolves a presented token, returning null when it is unknown, expired or revoked.
    /// </summary>
    Task<Principal?> ResolveAsync(string token, CancellationToken ct);

    /// <summary>
    /// Revokes a token by its identifier.
    /// </summary>
    Task RevokeAsync(long tokenId, CancellationToken ct);
}
