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
/// An account as it is stored.
/// </summary>
public sealed record PrincipalRecord(
    long Id,
    string Name,
    string DisplayName,
    PrincipalKind Kind,
    Role Role,
    bool IsEnabled,
    IReadOnlySet<string> Extra)
{
    /// <summary>
    /// The rights the account holds: the ones its role carries and the ones granted on top.
    /// </summary>
    public IReadOnlySet<string> Scopes =>
        Roles.Scopes(Role).Concat(Extra).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// Keeps the accounts of the panel.
/// </summary>
public interface IPrincipals
{
    /// <summary>
    /// Returns every account, by name.
    /// </summary>
    Task<IReadOnlyList<PrincipalRecord>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Returns an account by name, or null when there is none.
    /// </summary>
    Task<PrincipalRecord?> FindAsync(string name, CancellationToken ct);

    /// <summary>
    /// Returns an account by identifier, or null when there is none.
    /// </summary>
    Task<PrincipalRecord?> FindAsync(long id, CancellationToken ct);

    /// <summary>
    /// Returns the account a host user is registered as, or null when it is not registered.
    /// </summary>
    Task<PrincipalRecord?> FindByHostUserAsync(string userName, CancellationToken ct);

    /// <summary>
    /// Tells whether an enabled administrator already exists.
    /// </summary>
    Task<bool> HasAdminAsync(CancellationToken ct);

    /// <summary>
    /// Adds an account of the panel.
    /// </summary>
    Task<PrincipalRecord> AddAsync(string name, string displayName, Role role, CancellationToken ct);

    /// <summary>
    /// Registers a host user as an account and returns it.
    /// </summary>
    Task<PrincipalRecord> RegisterHostUserAsync(string userName, uint uid, string displayName, Role role, CancellationToken ct);

    /// <summary>
    /// Records that a host user was seen, keeping its user id current.
    /// </summary>
    Task SeenAsync(long id, uint uid, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Sets the role of an account.
    /// </summary>
    Task SetRoleAsync(long id, Role role, CancellationToken ct);

    /// <summary>
    /// Replaces the rights granted on top of the role.
    /// </summary>
    Task SetExtraAsync(long id, IEnumerable<string> scopes, CancellationToken ct);

    /// <summary>
    /// Turns an account on or off.
    /// </summary>
    Task SetEnabledAsync(long id, bool enabled, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Removes an account with everything hanging off it.
    /// </summary>
    Task RemoveAsync(long id, CancellationToken ct);
}
