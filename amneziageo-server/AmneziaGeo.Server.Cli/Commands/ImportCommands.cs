using System.Globalization;
using System.Text.Json;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;

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

            var clients = Read(section.Value, endpoint.Id, prefix);
            code |= await TakeAsync(context, clients, name, ct).ConfigureAwait(false);
        }

        return code;
    }

    /// <summary>
    /// Returns the address a client carries in a range of the second family.
    /// </summary>
    public static string Sixth(string prefix, string address)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(address);

        var bare = address.Split('/')[0];
        var mark = bare.LastIndexOf('.');
        if (mark < 0 || !int.TryParse(bare[(mark + 1)..], out var last))
        {
            return string.Empty;
        }

        return $"{prefix}:{last.ToString("x", CultureInfo.InvariantCulture)}/128";
    }

    private static IReadOnlyList<TunnelClient> Read(JsonElement section, long configId, string? prefix)
    {
        var clients = new List<TunnelClient>();
        var number = 0;
        foreach (var record in section.EnumerateObject())
        {
            number++;
            var body = record.Value;
            var key = Text(body, "publicKey");
            if (key.Length == 0)
            {
                continue;
            }

            var addresses = new List<string>();
            if (body.TryGetProperty("allowedIPs", out var ranges) && ranges.ValueKind == JsonValueKind.Array)
            {
                addresses.AddRange(ranges.EnumerateArray().Select(one => one.GetString() ?? string.Empty));
            }

            if (prefix is { Length: > 0 })
            {
                addresses.AddRange(
                    addresses.ToArray().Select(one => Sixth(prefix, one)).Where(one => one.Length > 0));
            }

            clients.Add(new TunnelClient
            {
                ConfigId = configId,
                Name = ClientImport.Name(Text(body, "email") is { Length: > 0 } email ? email : record.Name, number),
                PrivateKey = Text(body, "privateKey"),
                PublicKey = key,
                PresharedKey = Text(body, "preSharedKey"),
                Address = [.. addresses.Where(one => one.Length > 0)],
                IsEnabled = !body.TryGetProperty("enable", out var enabled) || enabled.ValueKind != JsonValueKind.False,
            });
        }

        return clients;
    }

    private static async Task<int> TakeAsync(
        Context context,
        IReadOnlyList<TunnelClient> clients,
        string name,
        CancellationToken ct)
    {
        var added = 0;
        var held = 0;
        var refused = 0;
        foreach (var client in clients)
        {
            var result = await context.Clients.ImportAsync(client, ct).ConfigureAwait(false);
            if (result.IsOk)
            {
                added++;
                if (!string.Equals(result.Record!.Name, client.Name.Trim(), StringComparison.Ordinal))
                {
                    Terminal.Say($"{name}: {client.Name} taken as {result.Record.Name}");
                }

                continue;
            }

            if (result.Outcome is ClientOutcome.KeyTaken)
            {
                held++;

                continue;
            }

            refused++;
            Terminal.Say($"{name}: {client.Name} refused, {result.Message}");
        }

        Terminal.Say($"{name}: {added} taken, {held} already held, {refused} refused");

        return refused > 0 ? 1 : 0;
    }

    private static async Task<ServerConfig?> FindAsync(Context context, string name, CancellationToken ct)
    {
        var held = await context.Configs.ListAsync(ct).ConfigureAwait(false);

        return held.FirstOrDefault(config => string.Equals(config.Name, name, StringComparison.Ordinal));
    }

    private static string Text(JsonElement body, string name) =>
        body.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString() ?? string.Empty
            : string.Empty;

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
