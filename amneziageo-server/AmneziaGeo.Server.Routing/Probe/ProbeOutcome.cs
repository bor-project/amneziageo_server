namespace AmneziaGeo.Server.Routing.Probe;

/// <summary>
/// How a probe ended.
/// </summary>
public enum ProbeOutcome
{
    /// <summary>
    /// The name server answered through the outbound.
    /// </summary>
    Reached,

    /// <summary>
    /// Nothing came back before the probe gave up.
    /// </summary>
    Missed,

    /// <summary>
    /// The probe never went out, so the verdict stands as it was.
    /// </summary>
    Skipped,
}
