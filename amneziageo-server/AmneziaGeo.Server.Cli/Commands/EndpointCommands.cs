using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that hold the ports of the endpoints open in the firewall of the host.
/// </summary>
public static class EndpointCommands
{
    /// <summary>
    /// Runs one endpoint command and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        return args.At(1) switch
        {
            "list" => await ListAsync(context, ct).ConfigureAwait(false),
            "open" => await OpenedAsync(context, args, opened: true, ct).ConfigureAwait(false),
            "close" => await OpenedAsync(context, args, opened: false, ct).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Names the endpoints and whether their ports are held open in the firewall.
    /// </summary>
    public static async Task<int> ListAsync(Context context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var held = await context.Configs.ListAsync(ct).ConfigureAwait(false);
        if (held.Count == 0)
        {
            Terminal.Say("there are no endpoints");

            return 0;
        }

        foreach (var config in held)
        {
            var carried = config.WebSocket ? $", websocket on TCP {ConfigServices.Port(config)}" : string.Empty;
            Terminal.Say($"{config.Name}: port {config.ListenPort}, {(config.Opened ? "open" : "closed")} in the firewall{carried}");
        }

        return 0;
    }

    /// <summary>
    /// Holds the port of an endpoint open in the firewall, or lets it stay closed.
    /// </summary>
    public static async Task<int> OpenedAsync(Context context, Arguments args, bool opened, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        var name = (args.At(2) ?? Terminal.Ask("endpoint: ")).Trim();
        if (name.Length == 0)
        {
            return Refuse("name the endpoint to " + (opened ? "open" : "close"));
        }

        var found = await FindAsync(context, name, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Refuse("there is no such endpoint");
        }

        var result = await context.Configs.ChangeAsync(found.Id, found with { Opened = opened }, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Refuse(result.Message);
        }

        Terminal.Say($"{name}: port {(opened ? "open" : "closed")} in the firewall, it takes hold when the service restarts");

        return 0;
    }

    private static async Task<ServerConfig?> FindAsync(Context context, string name, CancellationToken ct)
    {
        var held = await context.Configs.ListAsync(ct).ConfigureAwait(false);

        return held.FirstOrDefault(config => string.Equals(config.Name, name, StringComparison.Ordinal));
    }

    private static int Refuse(string message)
    {
        Terminal.Fail(message);

        return 2;
    }

    private static int Usage() => Refuse("""
        usage:
          endpoint list name the endpoints and their ports in the firewall
          endpoint open <name> hold the port of the endpoint open in the firewall
          endpoint close <name> let the port of the endpoint stay closed
        """);
}
