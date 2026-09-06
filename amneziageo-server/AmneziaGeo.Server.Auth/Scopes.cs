namespace AmneziaGeo.Server.Auth;

/// <summary>
/// Names of the rights a principal can hold.
/// </summary>
public static class Scopes
{
    /// <summary>
    /// The claim a right is carried as.
    /// </summary>
    public const string ClaimType = "scope";

    public const string ReadState = "state:read";
    public const string ManageClients = "clients:write";
    public const string ManageInterfaces = "interfaces:write";
    public const string ManageAccess = "access:write";

    /// <summary>
    /// The right to replace one own password, held even by an account that has to change it.
    /// </summary>
    public const string ChangePassword = "password:change";

    /// <summary>
    /// Every right that a role can carry.
    /// </summary>
    public static readonly string[] All = [ReadState, ManageClients, ManageInterfaces, ManageAccess];

    /// <summary>
    /// Rights that change a live tunnel and are refused to a caller without a fresh login.
    /// </summary>
    public static readonly string[] Sensitive = [ManageInterfaces, ManageAccess];

    /// <summary>
    /// Tells whether a name is a right the server knows.
    /// </summary>
    public static bool Known(string scope) => Array.IndexOf(All, scope) >= 0;
}
