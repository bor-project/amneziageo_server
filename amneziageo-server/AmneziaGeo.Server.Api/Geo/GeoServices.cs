using AmneziaGeo.Server.Geo;

namespace AmneziaGeo.Server.Api.Geo;

/// <summary>
/// How the server keeps its geo databases.
/// </summary>
public sealed class GeoOptions
{
    /// <summary>
    /// How often the sources are downloaded again, in hours; zero leaves it to the panel.
    /// </summary>
    public double UpdateHours { get; set; } = GeoDefaults.UpdateEvery.TotalHours;
}

/// <summary>
/// Wires the geo download and its schedule into the container.
/// </summary>
public static class GeoServices
{
    /// <summary>
    /// Registers the geo download, its settings and the schedule that runs it.
    /// </summary>
    public static IServiceCollection AddGeo(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new GeoOptions();
        configuration.GetSection("Geo").Bind(options);

        services.AddSingleton(options);
        services.AddScoped<GeoRefresher>();
        services.AddHttpClient<GeoDownloader>(http =>
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AmneziaGeo-Server"));

        services.AddHostedService<GeoSchedule>();

        return services;
    }
}
