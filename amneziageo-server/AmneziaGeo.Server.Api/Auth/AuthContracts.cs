using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// A password login as the interface sends it.
/// </summary>
public sealed record LoginRequest(string? User, string? Password);

/// <summary>
/// A refresh token handed back for a fresh pair.
/// </summary>
public sealed record RefreshRequest(string? Refresh);

/// <summary>
/// A password replacement as the interface sends it.
/// </summary>
public sealed record PasswordRequest(string? Current, string? Next);

/// <summary>
/// The account behind a session.
/// </summary>
public sealed record AccountResponse(string Name, string DisplayName, string Role, string Scheme, string[] Scopes);

/// <summary>
/// The tokens a login hands back.
/// </summary>
public sealed record SessionResponse(
    string Access,
    string? Refresh,
    int ExpiresIn,
    bool MustChangePassword,
    AccountResponse User);

/// <summary>
/// Builds the account and session answers from what the login service returned.
/// </summary>
public static class AuthAnswers
{
    /// <summary>
    /// Describes an account as the interface reads it.
    /// </summary>
    public static AccountResponse Account(AccountView view, AuthScheme scheme, IEnumerable<string> scopes) =>
        new(view.Record.Name, view.Record.DisplayName, view.Role, scheme.ToString(), [.. scopes.Order(StringComparer.Ordinal)]);

    /// <summary>
    /// Describes a session as the interface reads it.
    /// </summary>
    public static SessionResponse Session(LoginResult result, AccountView view, AuthOptions options) =>
        new(
            result.Access!,
            result.Refresh,
            (int)options.AccessLifetime.TotalSeconds,
            result.MustChangePassword,
            Account(view, result.Principal!.Scheme, result.Principal.Scopes));
}
