namespace AmneziaGeo.Server.Routing.Probe;

/// <summary>
/// What the last probes of one outbound came to.
/// </summary>
/// <param name="IsReached">Whether the outbound carries the probe.</param>
/// <param name="Falls">How many probes missed in a row.</param>
/// <param name="At">When the last probe went out.</param>
public sealed record ProbeReading(bool IsReached, int Falls, DateTimeOffset At);
