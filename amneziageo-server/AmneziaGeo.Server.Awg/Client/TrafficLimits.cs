namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Limits the speed and traffic of a client.
/// </summary>
public sealed record TrafficLimits(
    bool Enabled = false,
    long UploadBitsPerSecond = 0,
    long DownloadBitsPerSecond = 0,
    long QuotaBytes = 0,
    string Period = TrafficLimitPeriod.Month,
    long ExceededUploadBitsPerSecond = 0,
    long ExceededDownloadBitsPerSecond = 0,
    string QuotaAction = TrafficQuotaAction.Throttle);

/// <summary>
/// Names the calendar periods of a traffic quota.
/// </summary>
public static class TrafficLimitPeriod
{
    public const string Day = "day";

    public const string Week = "week";

    public const string Month = "month";
}

/// <summary>
/// Names the actions of a spent traffic quota.
/// </summary>
public static class TrafficQuotaAction
{
    public const string Block = "block";

    public const string Throttle = "throttle";
}

/// <summary>
/// Validates the limits of client traffic.
/// </summary>
public static class TrafficLimitsRules
{
    public const long MinBitsPerSecond = 8;

    public const long MaxBitsPerSecond = 1_000_000_000_000;

    public const long MaxQuotaBytes = 1L << 50;

    /// <summary>
    /// Returns the refusal of traffic limits.
    /// </summary>
    public static ClientFault? Check(TrafficLimits? limits)
    {
        if (limits is null)
        {
            return null;
        }

        if (!Rate(limits.UploadBitsPerSecond)
            || !Rate(limits.DownloadBitsPerSecond)
            || !Rate(limits.ExceededUploadBitsPerSecond)
            || !Rate(limits.ExceededDownloadBitsPerSecond))
        {
            return new ClientFault("bad-traffic-rate", $"the speed is 0 or lies within {MinBitsPerSecond} to {MaxBitsPerSecond} bits per second");
        }

        if (limits.QuotaBytes is < 0 or > MaxQuotaBytes)
        {
            return new ClientFault("bad-traffic-quota", $"the quota lies outside 0 to {MaxQuotaBytes} bytes");
        }

        if (limits.Period is not (TrafficLimitPeriod.Day or TrafficLimitPeriod.Week or TrafficLimitPeriod.Month))
        {
            return new ClientFault("bad-traffic-period", "the quota period is day, week or month");
        }

        if (limits.QuotaAction is not (TrafficQuotaAction.Block or TrafficQuotaAction.Throttle))
        {
            return new ClientFault("bad-traffic-quota-action", "the quota action is block or throttle");
        }

        return limits.Enabled && limits.QuotaBytes > 0 && limits.QuotaAction == TrafficQuotaAction.Throttle
            && (limits.ExceededUploadBitsPerSecond == 0 || limits.ExceededDownloadBitsPerSecond == 0)
            ? new ClientFault("bad-traffic-exceeded-rate", "a traffic quota requires positive upload and download speeds after it is spent")
            : null;
    }

    private static bool Rate(long rate) => rate == 0 || rate is >= MinBitsPerSecond and <= MaxBitsPerSecond;
}

/// <summary>
/// Resolves the traffic limits of a client.
/// </summary>
public static class TrafficLimitPolicy
{
    /// <summary>
    /// Returns the enabled limits inherited or overridden by a client.
    /// </summary>
    public static TrafficLimits? Resolve(TunnelClient client, ClientTemplate? template)
    {
        ArgumentNullException.ThrowIfNull(client);

        var limits = client.OverrideLimits ? client.Limits : template?.Limits;

        return limits is { Enabled: true } ? limits : null;
    }
}
