namespace AmneziaGeo.Server.Auth;

/// <summary>
/// The role the server carries on its own.
/// </summary>
public static class Roles
{
    /// <summary>
    /// The role that carries every right the server knows.
    /// </summary>
    public const string Admin = "admin";

    /// <summary>
    /// The name the panel shows for the built in role.
    /// </summary>
    public const string AdminTitle = "Administrator";

    /// <summary>
    /// Tells whether a role is the one the server keeps as it is.
    /// </summary>
    public static bool IsBuiltin(string? name) =>
        string.Equals(name, Admin, StringComparison.OrdinalIgnoreCase);
}
