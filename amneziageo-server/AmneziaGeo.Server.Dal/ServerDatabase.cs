using System.Security.Claims;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Geo.Files;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Wires the database, the identity stores and the built in role together.
/// </summary>
public static class ServerDatabase
{
    /// <summary>
    /// Environment variable that moves the database off its default path.
    /// </summary>
    public const string PathVariable = "AMNEZIAGEO_DB";

    private const string DefaultFile = "/var/lib/amneziageo-server/server.db";

    /// <summary>
    /// Returns the path the server keeps its database at.
    /// </summary>
    public static string DefaultPath() =>
        Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } set ? set : DefaultFile;

    /// <summary>
    /// Registers the database, the identity stores and the account services.
    /// </summary>
    public static IServiceCollection AddServerDatabase(this IServiceCollection services, string path, AuthOptions options, string? geoPath = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddDbContext<AppDbContext>(builder => builder.UseSqlite($"Data Source={path}"));

        services.AddIdentityCore<AppUser>(identity =>
            {
                identity.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyz0123456789._-";
                identity.User.RequireUniqueEmail = false;
                identity.Password.RequiredLength = options.MinimumPasswordLength;
                identity.Password.RequireDigit = false;
                identity.Password.RequireLowercase = false;
                identity.Password.RequireUppercase = false;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = options.FailedAttempts;
                identity.Lockout.DefaultLockoutTimeSpan = options.LockDuration;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<AccessResolver>();
        services.AddScoped<AccountManager>();
        services.AddScoped<RoleCatalog>();
        services.AddSingleton<IGeoFileStore>(new DiskGeoFiles(geoPath ?? DiskGeoFiles.PathNear(path)));
        services.AddScoped<ConfigStore>();
        services.AddScoped<ClientStore>();
        services.AddScoped<TrafficStore>();
        services.AddScoped<GeoStore>();
        services.AddScoped<OutboundStore>();
        services.AddScoped<RouteStore>();
        services.AddScoped<BalanceStore>();
        services.AddScoped<DnsStore>();
        services.AddScoped<DnsStandingStore>();
        services.AddScoped<PanelStore>();
        services.AddScoped<TemplateStore>();
        services.AddScoped<SubscriptionStore>();
        services.AddScoped<DatabaseBackup>();
        services.AddScoped<IRefreshTokens, RefreshTokenStore>();
        services.AddScoped<IApiTokens, ApiTokenStore>();
        services.AddScoped<IAuditLog, AuditStore>();
        services.AddScoped<LoginService>();
        services.AddScoped<ApiTokenManager>();

        return services;
    }

    /// <summary>
    /// Brings the schema up to date, hides the files of the database from other users and puts the built in role in
    /// place.
    /// </summary>
    public static async Task PrepareAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var database = db.Database;
        await database.MigrateAsync(ct).ConfigureAwait(false);
        DatabaseFiles.Hide(
            database.GetDbConnection().DataSource,
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseFiles).FullName!));
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var geo = scope.ServiceProvider.GetRequiredService<GeoStore>();
        var outbounds = scope.ServiceProvider.GetRequiredService<OutboundStore>();
        var templates = scope.ServiceProvider.GetRequiredService<TemplateStore>();
        await db.AloneAsync(
            async () =>
            {
                await SeedAsync(roles).ConfigureAwait(false);
                await geo.SeedAsync(ct).ConfigureAwait(false);
                await outbounds.SeedAsync(ct).ConfigureAwait(false);
                await templates.SeedAsync(ct).ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the built in role and keeps its claims level with the rights the server knows.
    /// </summary>
    public static async Task SeedAsync(RoleManager<AppRole> roles)
    {
        var role = await roles.FindByNameAsync(Roles.Admin).ConfigureAwait(false);
        if (role is null)
        {
            role = new AppRole(Roles.Admin)
            {
                Title = Roles.AdminTitle,
                IsBuiltin = true,
                CreatedUtc = DateTimeOffset.UtcNow,
            };

            await roles.CreateAsync(role).ConfigureAwait(false);
        }
        else if (!role.IsBuiltin)
        {
            role.IsBuiltin = true;
            await roles.UpdateAsync(role).ConfigureAwait(false);
        }

        var held = (await roles.GetClaimsAsync(role).ConfigureAwait(false))
            .Where(claim => claim.Type == Scopes.ClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var scope in Scopes.All.Where(scope => !held.Contains(scope)))
        {
            await roles.AddClaimAsync(role, new Claim(Scopes.ClaimType, scope)).ConfigureAwait(false);
        }
    }
}
