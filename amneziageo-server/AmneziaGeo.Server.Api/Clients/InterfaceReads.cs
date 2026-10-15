using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// Reads the interfaces of the endpoints for the guard and the meter: what one of them read within a round the other
/// takes as it is.
/// </summary>
public sealed class InterfaceReads : IAwgDevices
{
    /// <summary>
    /// The key the guard and the meter take the reads under.
    /// </summary>
    public const string Key = "rounds";

    private static readonly TimeSpan Fresh = TimeSpan.FromMilliseconds(1900);

    private readonly IAwgDevices _devices;

    private readonly TimeProvider _time;

    private readonly Lock _sync = new();

    private readonly Dictionary<string, Reading> _read = new(StringComparer.Ordinal);

    /// <summary>
    /// ctor
    /// </summary>
    public InterfaceReads(IAwgDevices devices, TimeProvider time)
    {
        _devices = devices;
        _time = time;
    }

    /// <summary>
    /// Returns the names of the interfaces the host carries.
    /// </summary>
    public IReadOnlyList<string> Names() => _devices.Names();

    /// <summary>
    /// Reads one interface, taking what was read of it within the round as it is.
    /// </summary>
    public AwgDevice? Find(string name)
    {
        var now = _time.GetUtcNow();
        lock (_sync)
        {
            if (_read.TryGetValue(name, out var held) && now - held.At < Fresh)
            {
                return held.Device;
            }
        }

        var device = _devices.Find(name);
        lock (_sync)
        {
            _read[name] = new Reading(now, device);
        }

        return device;
    }

    /// <summary>
    /// Reads every interface the host carries.
    /// </summary>
    public IReadOnlyList<AwgDevice> List() => _devices.List();

    /// <summary>
    /// Puts a change on an interface and forgets what was read of it.
    /// </summary>
    public void Apply(AwgUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_sync)
        {
            _read.Remove(update.Name);
        }

        _devices.Apply(update);
    }

    private sealed record Reading(DateTimeOffset At, AwgDevice? Device);
}
