using System.Net;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// The addresses the interfaces of the endpoints carry, read again now and then.
/// </summary>
public sealed class TunnelAddresses
{
    /// <summary>
    /// How long a reading of the addresses is taken as current.
    /// </summary>
    public static readonly TimeSpan Life = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;

    private readonly TimeProvider _time;

    private readonly Lock _sync = new();

    private HashSet<IPAddress> _held = [];

    private DateTimeOffset _read;

    /// <summary>
    /// ctor
    /// </summary>
    public TunnelAddresses(IServiceScopeFactory scopes, TimeProvider? time = null)
    {
        _scopes = scopes;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Tells whether an address belongs to an interface of the panel.
    /// </summary>
    public async Task<bool> HoldsAsync(IPAddress? address, CancellationToken ct)
    {
        if (address is null)
        {
            return false;
        }

        var now = _time.GetUtcNow();
        var wanted = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        lock (_sync)
        {
            if (_read + Life > now)
            {
                return _held.Contains(wanted);
            }
        }

        var found = await ReadAsync(ct).ConfigureAwait(false);
        lock (_sync)
        {
            _held = found;
            _read = now;

            return _held.Contains(wanted);
        }
    }

    private async Task<HashSet<IPAddress>> ReadAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var configs = await scope.ServiceProvider.GetRequiredService<ConfigStore>()
            .ListAsync(ct)
            .ConfigureAwait(false);

        return
        [
            .. configs
                .Where(config => config.IsEnabled)
                .SelectMany(config => config.Address)
                .Select(Address)
                .OfType<IPAddress>()
        ];
    }

    private static IPAddress? Address(string range) =>
        IPAddress.TryParse(range.Split('/')[0], out var found) ? found : null;
}

/// <summary>
/// Keeps the clients of the tunnels off the panel.
/// </summary>
public static class TunnelGate
{
    /// <summary>
    /// Adds the addresses of the interfaces the gate reads.
    /// </summary>
    public static IServiceCollection AddTunnelGate(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TunnelAddresses>();

        return services;
    }

    /// <summary>
    /// Answers nothing to a request that arrives on an address of an interface.
    /// </summary>
    public static WebApplication UseTunnelGate(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            var addresses = context.RequestServices.GetRequiredService<TunnelAddresses>();
            if (await addresses.HoldsAsync(context.Connection.LocalIpAddress, context.RequestAborted).ConfigureAwait(false))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return;
            }

            await next(context).ConfigureAwait(false);
        });

        return app;
    }
}
