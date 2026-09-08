using System.Security.Cryptography;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo.Files;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

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
/// A database in a temporary file with the identity services wired over it.
/// </summary>
public sealed class Bench : IDisposable
{
    private readonly string _path;

    private readonly string _geo;

    private readonly ECDsa _key;

    private readonly ServiceProvider _services;

    private readonly IServiceScope _scope;

    /// <summary>
    /// ctor
    /// </summary>
    public Bench(AuthOptions? options = null, DateTimeOffset? now = null)
    {
        _path = Path.Combine(Path.GetTempPath(), $"amneziageo-{Guid.NewGuid():N}.db");
        _geo = Path.Combine(Path.GetTempPath(), $"amneziageo-geo-{Guid.NewGuid():N}");
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Options = options ?? new AuthOptions();
        Clock = new Clock(now ?? new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
        Issuer = new TokenIssuer(_key, Options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ITokenIssuer>(Issuer);
        services.AddServerDatabase(_path, Options, _geo);

        _services = services.BuildServiceProvider();
        ServerDatabase.PrepareAsync(_services).GetAwaiter().GetResult();
        _scope = _services.CreateScope();

        Db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Users = _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        RoleStore = _scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        Access = _scope.ServiceProvider.GetRequiredService<AccessResolver>();
        Accounts = _scope.ServiceProvider.GetRequiredService<AccountManager>();
        Catalog = _scope.ServiceProvider.GetRequiredService<RoleCatalog>();
        Configs = _scope.ServiceProvider.GetRequiredService<ConfigStore>();
        Clients = _scope.ServiceProvider.GetRequiredService<ClientStore>();
        Geo = _scope.ServiceProvider.GetRequiredService<GeoStore>();
        Outbounds = _scope.ServiceProvider.GetRequiredService<OutboundStore>();
        Rules = _scope.ServiceProvider.GetRequiredService<RouteStore>();
        Balancers = _scope.ServiceProvider.GetRequiredService<BalanceStore>();
        Resolver = _scope.ServiceProvider.GetRequiredService<DnsStore>();
        GeoFiles = _scope.ServiceProvider.GetRequiredService<IGeoFileStore>();
        RefreshTokens = _scope.ServiceProvider.GetRequiredService<IRefreshTokens>();
        Audit = _scope.ServiceProvider.GetRequiredService<IAuditLog>();
        Login = _scope.ServiceProvider.GetRequiredService<LoginService>();
    }

    public AuthOptions Options { get; }

    public Clock Clock { get; }

    public AppDbContext Db { get; }

    public UserManager<AppUser> Users { get; }

    public RoleManager<AppRole> RoleStore { get; }

    public AccessResolver Access { get; }

    public AccountManager Accounts { get; }

    public RoleCatalog Catalog { get; }

    public ConfigStore Configs { get; }

    public ClientStore Clients { get; }

    public GeoStore Geo { get; }

    public OutboundStore Outbounds { get; }

    public RouteStore Rules { get; }

    public BalanceStore Balancers { get; }

    public DnsStore Resolver { get; }

    public IGeoFileStore GeoFiles { get; }

    public IRefreshTokens RefreshTokens { get; }

    public IAuditLog Audit { get; }

    public TokenIssuer Issuer { get; }

    public LoginService Login { get; }

    /// <summary>
    /// Adds an account with a password and returns it.
    /// </summary>
    public async Task<AppUser> UserAsync(string name, string password, string role = Roles.Admin, bool mustChange = false)
    {
        var user = new AppUser
        {
            UserName = name,
            DisplayName = name,
            Kind = PrincipalKind.Local,
            IsEnabled = true,
            MustChangePassword = mustChange,
            CreatedUtc = Clock.Now,
        };

        var created = await Users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        if (role is { Length: > 0 })
        {
            var given = await Users.AddToRoleAsync(user, role);
            Assert.True(given.Succeeded, string.Join("; ", given.Errors.Select(error => error.Description)));
        }

        return user;
    }

    /// <summary>
    /// Adds a role carrying the rights it is given.
    /// </summary>
    public async Task<AppRole> RoleAsync(string name, params string[] scopes)
    {
        var result = await Catalog.AddAsync(name, name, scopes, CancellationToken.None);
        Assert.True(result.IsOk, result.Message);

        return result.Record!;
    }

    /// <summary>
    /// Removes the database file.
    /// </summary>
    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
        _key.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(_geo))
        {
            Directory.Delete(_geo, recursive: true);
        }

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
