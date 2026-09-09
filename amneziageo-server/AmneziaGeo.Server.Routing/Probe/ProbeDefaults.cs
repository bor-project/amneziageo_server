namespace AmneziaGeo.Server.Routing.Probe;

/// <summary>
/// The values a probe starts with.
/// </summary>
public static class ProbeDefaults
{
    /// <summary>
    /// The name server a fresh outbound asks.
    /// </summary>
    public const string Address = "1.1.1.1";

    /// <summary>
    /// The name the probe asks for.
    /// </summary>
    public const string Name = "example.com";

    /// <summary>
    /// The longest address a probe asks.
    /// </summary>
    public const int MaxAddressLength = 64;

    /// <summary>
    /// How often the probe goes out, in seconds.
    /// </summary>
    public const int Every = 30;

    /// <summary>
    /// The shortest period between two probes, in seconds.
    /// </summary>
    public const int MinEvery = 5;

    /// <summary>
    /// The longest period between two probes, in seconds.
    /// </summary>
    public const int MaxEvery = 3600;

    /// <summary>
    /// How many probes miss in a row before the outbound stops carrying traffic.
    /// </summary>
    public const int Falls = 2;

    /// <summary>
    /// How long a probe waits for an answer.
    /// </summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often the server looks for outbounds due a probe.
    /// </summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
}
