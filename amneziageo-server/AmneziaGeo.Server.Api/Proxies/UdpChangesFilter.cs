using AmneziaGeo.Server.Routing.Udp;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Serializes UDP topology changes.
/// </summary>
public sealed class UdpChangesFilter(UdpPorts ports) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        using var hold = await ports.HoldAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
        return await next(context).ConfigureAwait(false);
    }
}
