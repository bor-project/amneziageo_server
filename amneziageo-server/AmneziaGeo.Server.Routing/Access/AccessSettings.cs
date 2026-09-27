namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// Whether the connection log runs and how long it keeps its records.
/// </summary>
public sealed record AccessSettings
{
    /// <summary>
    /// The settings a panel starts with.
    /// </summary>
    public static AccessSettings Default => new();

    /// <summary>
    /// Whether the new connections of the clients go to the log.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// How many days a record is kept.
    /// </summary>
    public int Days { get; init; } = AccessDefaults.Days;

    /// <summary>
    /// Returns why the settings cannot be taken, or null when they can.
    /// </summary>
    public string? Check() =>
        Days is < AccessDefaults.LeastDays or > AccessDefaults.MostDays
            ? $"a record is kept from {AccessDefaults.LeastDays} to {AccessDefaults.MostDays} days"
            : null;
}
