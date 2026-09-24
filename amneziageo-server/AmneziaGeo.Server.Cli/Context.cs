using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AmneziaGeo.Server.Cli;

/// <summary>
/// The database, the identity stores and the login service wired together for one run.
/// </summary>
public sealed class Context : IDisposable
{
    /// <summary>
    /// Environment variable that moves the signing key off its default path.
    /// </summary>
    public const string KeyVariable = "AMNEZIAGEO_SIGNING_KEY";

    /// <summary>
    /// Environment variable that moves the shortest password an account may carry.
    /// </summary>
    public const string PasswordLengthVariable = "AMNEZIAGEO_MIN_PASSWORD";

    /// <summary>
    /// Environment variable that sets what a host account is allowed to do, as the server reads it.
    /// </summary>
    public const string HostLoginVariable = "Auth__HostLogin";

    private readonly ServiceProvider _services;

    private readonly IServiceScope _scope;

    private readonly TokenIssuer _issuer;

    /// <summary>
    /// ctor
    /// </summary>
    private Context(ServiceProvider services, IServiceScope scope, AuthOptions options, TokenIssuer issuer, string path)
    {
        _services = services;
        _scope = scope;
        _issuer = issuer;

        Options = options;
        Path = path;

        Users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        Accounts = scope.ServiceProvider.GetRequiredService<AccountManager>();
        Catalog = scope.ServiceProvider.GetRequiredService<RoleCatalog>();
        Tokens = scope.ServiceProvider.GetRequiredService<ApiTokenManager>();
        Login = scope.ServiceProvider.GetRequiredService<LoginService>();
        Configs = scope.ServiceProvider.GetRequiredService<ConfigStore>();
        Clients = scope.ServiceProvider.GetRequiredService<ClientStore>();
        Panel = scope.ServiceProvider.GetRequiredService<PanelStore>();
    }

    public AuthOptions Options { get; }

    public string Path { get; }

    public UserManager<AppUser> Users { get; }

    public RoleManager<AppRole> Roles { get; }

    public AccountManager Accounts { get; }

    public RoleCatalog Catalog { get; }

    public ApiTokenManager Tokens { get; }

    public LoginService Login { get; }

    public ConfigStore Configs { get; }

    public ClientStore Clients { get; }

    public PanelStore Panel { get; }

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

        if (int.TryParse(Environment.GetEnvironmentVariable(PasswordLengthVariable), out var length) && length > 0)
        {
            options.MinimumPasswordLength = length;
        }

        if (Enum.TryParse<HostLogin>(Environment.GetEnvironmentVariable(HostLoginVariable), true, out var login))
        {
            options.HostLogin = login;
        }

        var path = databasePath ?? ServerDatabase.DefaultPath();
        var issuer = TokenIssuer.Open(options);
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenIssuer>(issuer);
        services.AddServerDatabase(path, options);

        var provider = services.BuildServiceProvider();
        ServerDatabase.PrepareAsync(provider).GetAwaiter().GetResult();

        return new Context(provider, provider.CreateScope(), options, issuer, path);
    }

    /// <summary>
    /// Releases the signing key and the services.
    /// </summary>
    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
        _issuer.Dispose();
    }
}
