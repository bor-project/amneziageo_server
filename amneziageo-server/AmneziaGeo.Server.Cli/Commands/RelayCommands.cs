using System.Net;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Relay;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The relay command of the utility.
/// </summary>
public static class RelayCommands
{
    /// <summary>
    /// Carries datagrams from a port to a target until the process is stopped.
    /// </summary>
    public static async Task<int> RunAsync(Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!ProxyRules.Target(args.Value("listen") ?? string.Empty, out var host, out var port))
        {
            return Usage();
        }

        if (!ProxyRules.Target(args.Value("target") ?? string.Empty, out var far, out var farPort))
        {
            return Usage();
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return Usage();
        }

        var target = await ResolveAsync(far, farPort, ct).ConfigureAwait(false);
        if (target is null)
        {
            Terminal.Fail($"'{far}' does not resolve");

            return 1;
        }

        using var relay = new UdpRelay(new IPEndPoint(address, port), target, Idle(args));
        Terminal.Say($"the relay carries {address}:{port} to {target}");
        await relay.RunAsync(ct).ConfigureAwait(false);

        return 0;
    }

    private static TimeSpan Idle(Arguments args)
    {
        var read = int.TryParse(args.Value("idle"), out var seconds) && seconds > 0 ? seconds : ProxyDefaults.Idle;

        return TimeSpan.FromSeconds(read);
    }

    private static async Task<IPEndPoint?> ResolveAsync(string host, int port, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return new IPEndPoint(address, port);
        }

        var found = await System.Net.Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

        return found.Length == 0 ? null : new IPEndPoint(found[0], port);
    }

    private static int Usage()
    {
        Terminal.Fail("""
            usage:
              amneziageo-server-cli relay --listen <address:port> --target <host:port> [--idle <seconds>]
            """);

        return 2;
    }
}
