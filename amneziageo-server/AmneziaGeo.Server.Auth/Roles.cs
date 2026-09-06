namespace AmneziaGeo.Server.Auth;

/// <summary>
/// The named sets of rights an account is given.
/// </summary>
public enum Role
{
    None = 0,
    Viewer = 1,
    Operator = 2,
    Admin = 3,
}

/// <summary>
/// Turns roles into the rights they carry.
/// </summary>
public static class Roles
{
    /// <summary>
    /// Returns the rights a role carries.
    /// </summary>
    public static IReadOnlyList<string> Scopes(Role role) => role switch
    {
        Role.Admin => AmneziaGeo.Server.Auth.Scopes.All,
        Role.Operator => [AmneziaGeo.Server.Auth.Scopes.ReadState, AmneziaGeo.Server.Auth.Scopes.ManageClients],
        Role.Viewer => [AmneziaGeo.Server.Auth.Scopes.ReadState],
        _ => [],
    };

    /// <summary>
    /// Reads a role written as text, falling back to none.
    /// </summary>
    public static Role Parse(string? text) =>
        Enum.TryParse<Role>(text, ignoreCase: true, out var role) ? role : Role.None;

    /// <summary>
    /// Writes a role as it is stored.
    /// </summary>
    public static string Text(Role role) => role.ToString().ToLowerInvariant();
}
