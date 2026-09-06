using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Cli;

/// <summary>
/// The database, the stores and the login service wired together for one run.
/// </summary>
public sealed class Context : IDisposable
{
    /// <summary>
    /// Environment variable that moves the signing key off its default path.
    /// </summary>
    public const string KeyVariable = "AMNEZIAGEO_SIGNING_KEY";

    private readonly TokenIssuer _issuer;

    /// <summary>
    /// ctor
    /// </summary>
    private Context(Db db, AuthOptions options, TokenIssuer issuer)
    {
        Db = db;
        Options = options;
        _issuer = issuer;

        Principals = new PrincipalStore(db);
        Passwords = new PasswordStore(db, options);
        RefreshTokens = new RefreshTokenStore(db, Principals, options);
        Audit = new AuditStore(db);
        Login = new LoginService(Principals, Passwords, RefreshTokens, issuer, Audit, options);
    }

    public Db Db { get; }

    public AuthOptions Options { get; }

    public IPrincipals Principals { get; }

    public IPasswords Passwords { get; }

    public IRefreshTokens RefreshTokens { get; }

    public IAuditLog Audit { get; }

    public LoginService Login { get; }

    /// <summary>
    /// Opens the database, brings its schema up to date and reads the signing key.
    /// </summary>
    public static Context Open(string? databasePath = null)
    {
        var options = new AuthOptions();
        if (Environment.GetEnvironmentVariable(KeyVariable) is { Length: > 0 } key)
        {
            options.SigningKeyPath = key;
        }

        var db = new Db(databasePath ?? Db.DefaultPath());
        db.Migrate();

        return new Context(db, options, TokenIssuer.Open(options));
    }

    /// <summary>
    /// Releases the signing key.
    /// </summary>
    public void Dispose() => _issuer.Dispose();
}
