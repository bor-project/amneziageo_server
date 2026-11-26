using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Api.Configs;

public sealed record ConfigIpv6Request(long? Id, string[]? Address, string? Ipv6AutoAddress);
public sealed record ConfigIpv6Response(bool Available, string Reason, IReadOnlyList<string> Address, string Ipv6AutoAddress);

public static partial class ConfigEndpoints
{
    private static async Task<IResult> Ipv6Async(ConfigIpv6Request request, IIpv6Probe probe, ConfigStore store, CancellationToken ct)
    {
        if (request.Id is { } id && await store.FindAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Refuse(404, "unknown-config", "the interface no longer exists");
        }
        var addresses = request.Address ?? [];
        if (addresses.Length > 64 || addresses.Any(one => !AwgAllowedIp.TryParse(one, out _)))
        {
            return Refuse(400, "bad-address", "the interface address is not a valid address range");
        }
        var reach = await probe.CheckAsync(ct).ConfigureAwait(false);
        if (reach.Reason is "probe-failed" or "unsupported")
        {
            return Refuse(503, "ipv6-check-failed", "IPv6 availability could not be checked");
        }
        var plan = ConfigIpv6.Refresh(addresses, request.Ipv6AutoAddress ?? string.Empty, reach.Available,
            await Ipv6ReservedAsync(store, request.Id, ct).ConfigureAwait(false));
        return !plan.HasSpace
            ? Refuse(409, "ipv6-address-unavailable", "no automatic IPv6 subnet is available")
            : Results.Ok(new ConfigIpv6Response(reach.Available, reach.Reason, plan.Address, plan.AutomaticAddress));
    }

    private static async Task<IReadOnlyList<string>> Ipv6ReservedAsync(ConfigStore store, long? self, CancellationToken ct)
    {
        var configs = await store.ListAsync(ct).ConfigureAwait(false);
        return [.. configs.Where(one => one.Id != self).SelectMany(one => one.Address),
            .. HostRoutes.Read(configs.FirstOrDefault(one => one.Id == self)?.Name).Where(one => !one.IsReject && one.PrefixLength > 0).Select(one => one.Range)];
    }
}
