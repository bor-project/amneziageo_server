using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// A long lived token as the interface reads it.
/// </summary>
public sealed record TokenResponse(
    long Id,
    string Name,
    string Role,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset? LastUsedUtc,
    string? LastAddress,
    bool IsExpired);

/// <summary>
/// A token just minted, with the only copy of its secret.
/// </summary>
public sealed record MintedResponse(TokenResponse Token, string Secret);

/// <summary>
/// A new token as the interface sends it.
/// </summary>
public sealed record TokenRequest(string? Name, string? Role, int? Days);

/// <summary>
/// Builds the token answers of the management routes.
/// </summary>
public static class TokenAnswers
{
    /// <summary>
    /// Describes a token as the interface reads it.
    /// </summary>
    public static TokenResponse Token(ApiTokenView view, DateTimeOffset now) => new(
        view.Id,
        view.Name,
        view.Role,
        view.CreatedUtc,
        view.ExpiresUtc,
        view.LastUsedUtc,
        view.LastAddress,
        view.IsExpired(now));
}
