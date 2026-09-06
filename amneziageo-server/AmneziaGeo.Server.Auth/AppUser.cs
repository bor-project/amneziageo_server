using Microsoft.AspNetCore.Identity;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// Where an account came from.
/// </summary>
public enum PrincipalKind
{
    Local = 0,
    Host = 1,
}

/// <summary>
/// An account of the panel.
/// </summary>
public sealed class AppUser : IdentityUser<long>
{
    /// <summary>
    /// The name the panel shows.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the account is of the panel or of the host.
    /// </summary>
    public PrincipalKind Kind { get; set; }

    /// <summary>
    /// Whether the account is allowed to sign in.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether the next sign in has to replace the password.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// When the account was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>
    /// When the account was switched off.
    /// </summary>
    public DateTimeOffset? DisabledUtc { get; set; }

    /// <summary>
    /// When the password was replaced.
    /// </summary>
    public DateTimeOffset? PasswordChangedUtc { get; set; }

    /// <summary>
    /// The host user the account stands for.
    /// </summary>
    public string? HostUserName { get; set; }

    /// <summary>
    /// The identifier the host gives that user.
    /// </summary>
    public long? HostUid { get; set; }

    /// <summary>
    /// When the host user was last seen.
    /// </summary>
    public DateTimeOffset? HostSeenUtc { get; set; }

    /// <summary>
    /// The name the account signs in with.
    /// </summary>
    public string Name => UserName ?? string.Empty;
}
