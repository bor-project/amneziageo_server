namespace AmneziaGeo.Server.Routing.Probe;

/// <summary>
/// Binds a check to its configuration.
/// </summary>
public readonly record struct ProbeAttempt(string Name, long Generation);

/// <summary>
/// Describes an accepted check.
/// </summary>
public readonly record struct ProbeCompletion(
    bool Accepted,
    bool CarriesChanged,
    bool Warning,
    bool Recovered,
    ProbeReading? Reading);
