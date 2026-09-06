namespace AmneziaGeo.Server.Auth;

/// <summary>
/// What a host account is allowed to do on its own.
/// </summary>
public enum HostLogin
{
    /// <summary>
    /// A host account signs in only when a group of the host maps it to a role.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Any host account signs in, taking the role its groups map to and no rights without one.
    /// </summary>
    Register = 1,

    /// <summary>
    /// Host accounts do not sign in at all.
    /// </summary>
    Off = 2,
}

/// <summary>
/// Lifetimes and identifiers the token issuer works by.
/// </summary>
public sealed class AuthOptions
{
    public string Issuer { get; set; } = "amneziageo-server";

    public string Audience { get; set; } = "amneziageo-server";

    /// <summary>
    /// How long an access token stays valid.
    /// </summary>
    public TimeSpan AccessLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a refresh token stays valid after it is issued.
    /// </summary>
    public TimeSpan RefreshLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// The ceiling a session cannot be extended past, however often it is refreshed.
    /// </summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How long a rotated refresh token keeps answering, so parallel requests do not look like theft.
    /// </summary>
    public TimeSpan RotationGrace { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Path of the signing key file.
    /// </summary>
    public string SigningKeyPath { get; set; } = "/etc/amneziageo-server/signing.pem";

    /// <summary>
    /// What a host account is allowed to do on its own.
    /// </summary>
    public HostLogin HostLogin { get; set; } = HostLogin.Strict;

    /// <summary>
    /// Groups of the host and the role each one grants.
    /// </summary>
    public Dictionary<string, Role> HostGroups { get; set; } = new(StringComparer.Ordinal)
    {
        ["amneziageo-admin"] = Role.Admin,
        ["amneziageo-operator"] = Role.Operator,
        ["amneziageo"] = Role.Viewer,
    };

    /// <summary>
    /// Whether the root account of the host is taken as an administrator.
    /// </summary>
    public bool RootIsAdmin { get; set; } = true;

    /// <summary>
    /// The groups of the host that map to a role, written out.
    /// </summary>
    public string HostGroupNames => string.Join(", ", HostGroups.Keys);

    /// <summary>
    /// How many wrong passwords an account takes before it locks.
    /// </summary>
    public int FailedAttempts { get; set; } = 10;

    /// <summary>
    /// How long an account stays locked after too many wrong passwords.
    /// </summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(15);
}
