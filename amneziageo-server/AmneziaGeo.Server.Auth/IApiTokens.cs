namespace AmneziaGeo.Server.Auth;

/// <summary>
/// A long lived token as the panel lists it.
/// </summary>
public sealed record ApiTokenView(
    long Id,
    string Name,
    string Role,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset? LastUsedUtc,
    string? LastAddress)
{
    /// <summary>
    /// Tells whether the token has run out.
    /// </summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresUtc is { } until && until <= now;
}

/// <summary>
/// A token just minted, with the only copy of its secret.
/// </summary>
public sealed record MintedToken(ApiTokenView Token, string Secret);

/// <summary>
/// Long lived tokens for machines, each acting by a role.
/// </summary>
public interface IApiTokens
{
    /// <summary>
    /// Mints a token that acts by a role, returning the only copy of its secret.
    /// </summary>
    Task<MintedToken> MintAsync(string name, string role, DateTimeOffset? expires, CancellationToken ct);

    /// <summary>
    /// Resolves a presented token, returning null when it is unknown, expired or its role is gone.
    /// </summary>
    Task<Principal?> ResolveAsync(string token, string? address, CancellationToken ct);

    /// <summary>
    /// Returns every token with the role it acts by.
    /// </summary>
    Task<IReadOnlyList<ApiTokenView>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Returns how many tokens act by a role.
    /// </summary>
    Task<int> CountAsync(string role, CancellationToken ct);

    /// <summary>
    /// Revokes a token by its identifier, returning false when there is none.
    /// </summary>
    Task<bool> RevokeAsync(long tokenId, CancellationToken ct);
}
