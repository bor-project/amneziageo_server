using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// Wires the database, the identity stores and the login service into the container.
/// </summary>
public static class ServerAuth
{
    /// <summary>
    /// Environment variable that moves the signing key off its default path.
    /// </summary>
    public const string KeyVariable = "AMNEZIAGEO_SIGNING_KEY";

    /// <summary>
    /// Registers the database, the identity stores and the login service.
    /// </summary>
    public static IServiceCollection AddServerAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = Read(configuration);
        var path = configuration["Database:Path"] is { Length: > 0 } set ? set : ServerDatabase.DefaultPath();

        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenIssuer>(_ => TokenIssuer.Open(options));
        services.AddServerDatabase(path, options, configuration["Geo:Path"]);
        services.AddScoped(provider => new HostUsers(
            provider.GetRequiredService<IHostCommands>(),
            options,
            path));

        return services;
    }

    /// <summary>
    /// Brings the schema of the database up to date and puts the built in role in place.
    /// </summary>
    public static WebApplication MigrateDatabase(this WebApplication app)
    {
        ServerDatabase.PrepareAsync(app.Services).GetAwaiter().GetResult();

        return app;
    }

    private static AuthOptions Read(IConfiguration configuration)
    {
        var options = new AuthOptions();
        configuration.GetSection("Auth").Bind(options);

        if (Environment.GetEnvironmentVariable(KeyVariable) is { Length: > 0 } key)
        {
            options.SigningKeyPath = key;
        }

        return options;
    }
}
