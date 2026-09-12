using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// An account of the panel as the interface reads it.
/// </summary>
public sealed record UserResponse(
    string Name,
    string DisplayName,
    string Kind,
    string Role,
    bool Enabled,
    bool HasPassword,
    string[] Scopes,
    string HostUser,
    bool HasKey);

/// <summary>
/// A new account as the interface sends it.
/// </summary>
public sealed record UserCreateRequest(
    string? Name,
    string? DisplayName,
    string? Role,
    string? Password,
    bool MustChangePassword = true,
    bool Host = false,
    string? PublicKey = null);

/// <summary>
/// The parts of an account the interface changes.
/// </summary>
public sealed record UserPatchRequest(string? Role, bool? Enabled, string? PublicKey = null);

/// <summary>
/// A password an administrator sets on an account.
/// </summary>
public sealed record UserPasswordRequest(string? Password, bool MustChangePassword = true);

/// <summary>
/// Builds the account answers of the management routes.
/// </summary>
public static class UserAnswers
{
    /// <summary>
    /// Describes an account as the interface reads it.
    /// </summary>
    public static UserResponse User(AccountView view) => new(
        view.Record.Name,
        view.Record.DisplayName,
        view.Record.Kind.ToString().ToLowerInvariant(),
        view.Role,
        view.Record.IsEnabled,
        view.HasPassword,
        [.. view.Scopes.Order(StringComparer.Ordinal)],
        view.Record.HostUserName ?? string.Empty,
        view.Record.HostKey is { Length: > 0 });
}
