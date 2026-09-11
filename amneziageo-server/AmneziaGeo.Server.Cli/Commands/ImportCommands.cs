using System.Text.Json;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that take endpoints and clients from the files a host already carries.
/// </summary>
public static class ImportCommands
{
    /// <summary>
    /// Runs one import command and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.At(1) switch
        {
            "endpoint" => await EndpointAsync(context, args, ct).ConfigureAwait(false),
            "peers" => await PeersAsync(context, args, ct).ConfigureAwait(false),
            "clients" => await ClientsAsync(context, args, ct).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Takes an endpoint from the interface file of a host.
    /// </summary>
    public static async Task<int> EndpointAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.At(2) is not { Length: > 0 } path || !File.Exists(path))
        {
            return Refuse("name the interface file to read");
        }

        var name = args.Value("name") ?? Path.GetFileNameWithoutExtension(path);
        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var read = ConfigImport.Read(text, name);
        if (read.Config is null)
        {
            return Refuse(read.Fault!.Message);
        }

        var draft = read.Config with
        {
            Host = args.Value("host") ?? string.Empty,
            Dns = Parts(args.Value("dns")) is { Count: > 0 } servers ? servers : read.Config.Dns,
            AllowedIps = Parts(args.Value("allowed")) is { Count: > 0 } ranges ? ranges : read.Config.AllowedIps,
        };

        var held = await context.Configs.ListAsync(ct).ConfigureAwait(false);
        var found = held.FirstOrDefault(config => string.Equals(config.Name, name, StringComparison.Ordinal));
        var result = found is null
            ? await context.Configs.AddAsync(draft, ct).ConfigureAwait(false)
            : await context.Configs.ChangeAsync(found.Id, draft, ct).ConfigureAwait(false);

        if (!result.IsOk)
        {
            return Refuse(result.Message);
        }

        Terminal.Say($"{name}: port {result.Record!.ListenPort}, {string.Join(", ", result.Record.Address)}");

        return 0;
    }

    /// <summary>
    /// Takes the clients of an endpoint from the peers of an interface file.
    /// </summary>
    public static async Task<int> PeersAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.At(2) is not { Length: > 0 } path || !File.Exists(path))
        {
            return Refuse("name the interface file to read");
        }

        var name = args.Value("endpoint") ?? Path.GetFileNameWithoutExtension(path);
        var endpoint = await FindAsync(context, name, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return Refuse($"the panel holds no endpoint called '{name}'");
        }

        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

        return await TakeAsync(context, ClientImport.Peers(text, endpoint.Id), name, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes the clients of an endpoint from the file a host keeps them in.
    /// </summary>
    public static async Task<int> ClientsAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.At(2) is not { Length: > 0 } path || !File.Exists(path))
        {
            return Refuse("name the file the clients are kept in");
        }

        var wanted = args.Value("endpoint");
        var prefix = args.Value("v6");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
        var code = 0;
        foreach (var section in document.RootElement.EnumerateObject())
        {
            var name = wanted ?? section.Name;
            if (wanted is not null && !string.Equals(section.Name, wanted, StringComparison.Ordinal))
            {
                continue;
            }

            var endpoint = await FindAsync(context, name, ct).ConfigureAwait(false);
            if (endpoint is null)
            {
                Terminal.Say($"{name}: the panel holds no endpoint under this name");
                code = 1;

                continue;
            }

            var clients = ClientImport.Clients(section.Value, endpoint.Id, prefix);
            code |= await TakeAsync(context, clients, name, ct).ConfigureAwait(false);
        }

        return code;
    }

    private static async Task<int> TakeAsync(
        Context context,
        IReadOnlyList<TunnelClient> clients,
        string name,
        CancellationToken ct)
    {
        var report = await context.Clients.ImportAllAsync(clients, ct).ConfigureAwait(false);
        foreach (var renamed in report.Renamed)
        {
            Terminal.Say($"{name}: {renamed.From} taken as {renamed.To}");
        }

        foreach (var refused in report.Refused)
        {
            Terminal.Say($"{name}: {refused.Name} refused, {refused.Message}");
        }

        Terminal.Say($"{name}: {report.Taken} taken, {report.Held} already held, {report.Refused.Count} refused");

        return report.Refused.Count > 0 ? 1 : 0;
    }

    private static async Task<ServerConfig?> FindAsync(Context context, string name, CancellationToken ct)
    {
        var held = await context.Configs.ListAsync(ct).ConfigureAwait(false);

        return held.FirstOrDefault(config => string.Equals(config.Name, name, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Parts(string? text) =>
        text is null ? [] : ConfigLines.Parts(text);

    private static int Refuse(string message)
    {
        Terminal.Fail(message);

        return 2;
    }

    private static int Usage() => Refuse("""
        usage:
          import endpoint <file.conf> [--name <name>] [--host <address>] [--dns <list>] [--allowed <list>]
          import peers <file.conf> [--endpoint <name>]
          import clients <file.json> [--endpoint <name>] [--v6 <prefix>]
        """);
}
