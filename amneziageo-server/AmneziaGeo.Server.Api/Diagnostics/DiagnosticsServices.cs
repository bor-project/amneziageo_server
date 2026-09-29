using AmneziaGeo.Server.Core.Diagnostics;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// Wires the journal of the panel into the logging and the container.
/// </summary>
public static class DiagnosticsServices
{
    /// <summary>
    /// Registers the journal of the panel as a log provider, its file beside the database and the service that fills it.
    /// </summary>
    public static WebApplicationBuilder AddJournal(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var database = builder.Configuration["Database:Path"] is { Length: > 0 } set ? set : ServerDatabase.DefaultPath();
        var journal = new PanelJournal(JournalDefaults.View, TimeProvider.System);
        builder.Logging.AddProvider(journal);
        builder.Services.AddSingleton(journal);
        builder.Services.AddSingleton(new JournalRecords(JournalRecords.PathNear(database)));
        builder.Services.AddSingleton(JournalLimits.From(builder.Configuration));
        builder.Services.AddSingleton<JournalHost>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<JournalHost>());

        return builder;
    }
}
