using System.Security.Cryptography;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

/// <summary>
/// A clock the tests move by hand.
/// </summary>
public sealed class Clock : TimeProvider
{
    /// <summary>
    /// ctor
    /// </summary>
    public Clock(DateTimeOffset now)
    {
        Now = now;
    }

    /// <summary>
    /// The instant the clock answers with.
    /// </summary>
    public DateTimeOffset Now { get; set; }

    /// <summary>
    /// Returns the instant the clock stands at.
    /// </summary>
    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>
    /// Moves the clock forward.
    /// </summary>
    public void Pass(TimeSpan span) => Now += span;
}

/// <summary>
/// A database in a temporary file with the stores wired over it.
/// </summary>
public sealed class Bench : IDisposable
{
    private readonly string _path;

    private readonly ECDsa _key;

    /// <summary>
    /// ctor
    /// </summary>
    public Bench(AuthOptions? options = null, DateTimeOffset? now = null)
    {
        _path = Path.Combine(Path.GetTempPath(), $"amneziageo-{Guid.NewGuid():N}.db");
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Options = options ?? new AuthOptions();
        Clock = new Clock(now ?? new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
        Db = new Db(_path);
        Db.Migrate();

        Principals = new PrincipalStore(Db);
        Passwords = new PasswordStore(Db, Options);
        RefreshTokens = new RefreshTokenStore(Db, Principals, Options);
        Issuer = new TokenIssuer(_key, Options);
        Audit = new AuditStore(Db);
        Login = new LoginService(Principals, Passwords, RefreshTokens, Issuer, Audit, Options, Clock);
    }

    public AuthOptions Options { get; }

    public Clock Clock { get; }

    public Db Db { get; }

    public PrincipalStore Principals { get; }

    public PasswordStore Passwords { get; }

    public RefreshTokenStore RefreshTokens { get; }

    public TokenIssuer Issuer { get; }

    public AuditStore Audit { get; }

    public LoginService Login { get; }

    /// <summary>
    /// Adds an account with a password and returns it.
    /// </summary>
    public async Task<PrincipalRecord> UserAsync(string name, string password, Role role = Role.Viewer, bool mustChange = false)
    {
        var record = await Principals.AddAsync(name, name, role, CancellationToken.None);
        await Passwords.SetAsync(record.Id, password, mustChange, Clock.Now, CancellationToken.None);

        return record;
    }

    /// <summary>
    /// Removes the database file.
    /// </summary>
    public void Dispose()
    {
        _key.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = _path + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
