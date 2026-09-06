namespace AmneziaGeo.Server.Auth;

/// <summary>
/// What checking a password produced.
/// </summary>
public enum PasswordCheck
{
    Ok = 0,
    Wrong = 1,
    NotSet = 2,
    Locked = 3,
    MustChange = 4,
}

/// <summary>
/// Keeps the passwords of accounts.
/// </summary>
public interface IPasswords
{
    /// <summary>
    /// Sets the password of an account and clears its failure count.
    /// </summary>
    Task SetAsync(long principalId, string password, bool mustChange, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Checks a password, counting failures and locking the account past the allowed number.
    /// </summary>
    Task<PasswordCheck> CheckAsync(long principalId, string password, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Tells whether an account carries a password.
    /// </summary>
    Task<bool> HasAsync(long principalId, CancellationToken ct);

    /// <summary>
    /// Drops the password of an account.
    /// </summary>
    Task ClearAsync(long principalId, CancellationToken ct);
}
