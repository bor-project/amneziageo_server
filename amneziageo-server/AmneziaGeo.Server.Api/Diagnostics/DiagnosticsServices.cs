namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// Wires the journal of the panel into the logging and the container.
/// </summary>
public static class DiagnosticsServices
{
    private const int Capacity = 500;

    /// <summary>
    /// Registers the journal of the panel as a log provider.
    /// </summary>
    public static WebApplicationBuilder AddJournal(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var journal = new PanelJournal(Capacity, TimeProvider.System);
        builder.Logging.AddProvider(journal);
        builder.Services.AddSingleton(journal);

        return builder;
    }
}
