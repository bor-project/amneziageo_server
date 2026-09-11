namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// What asking to hold a configuration produced.
/// </summary>
/// <param name="IsHeld">Whether the device holds the configuration.</param>
/// <param name="Until">When the hold ends unless it is repeated.</param>
/// <param name="Since">When the device that holds the configuration took it.</param>
public sealed record HoldAnswer(bool IsHeld, DateTimeOffset Until, DateTimeOffset Since);

/// <summary>
/// Which device holds which configuration of the subscriptions, as the applications report it.
/// </summary>
public sealed class DeviceHolds
{
    /// <summary>
    /// How long a hold lasts when it is not repeated.
    /// </summary>
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(150);

    private readonly Dictionary<string, Hold> _held = new(StringComparer.Ordinal);

    private readonly Lock _sync = new();

    /// <summary>
    /// Lets a device hold a configuration unless another device holds it.
    /// </summary>
    public HoldAnswer Take(string key, string device, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(device);

        lock (_sync)
        {
            var held = _held.GetValueOrDefault(key);
            var live = held is not null && held.Until > now;
            if (live && !string.Equals(held!.Device, device, StringComparison.Ordinal))
            {
                return new HoldAnswer(false, held.Until, held.Since);
            }

            var taken = new Hold(device, now + Lease, live ? held!.Since : now);
            _held[key] = taken;

            return new HoldAnswer(true, taken.Until, taken.Since);
        }
    }

    /// <summary>
    /// Lets a configuration go when the device that holds it asks.
    /// </summary>
    public void Drop(string key, string device)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(device);

        lock (_sync)
        {
            if (_held.TryGetValue(key, out var held) && string.Equals(held.Device, device, StringComparison.Ordinal))
            {
                _held.Remove(key);
            }
        }
    }

    private sealed record Hold(string Device, DateTimeOffset Until, DateTimeOffset Since);
}
