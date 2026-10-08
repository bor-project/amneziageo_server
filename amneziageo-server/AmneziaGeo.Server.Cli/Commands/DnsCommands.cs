using System.Globalization;
using System.Text.Json;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that read the resolver of the clients, turn it on and off and name its name servers.
/// </summary>
public static class DnsCommands
{
    /// <summary>
    /// The word that stands for the name servers a panel starts with.
    /// </summary>
    public const string Usual = "default";

    private static readonly char[] Breaks = [',', ' '];

    /// <summary>
    /// Runs one command of the resolver and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        return args.At(1) switch
        {
            "show" => await ShowAsync(context, ct).ConfigureAwait(false),
            "get" => await GetAsync(context, args, ct).ConfigureAwait(false),
            "set" => await SetAsync(context, args, ct).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Prints whether the resolver answers the clients and how it asks outside.
    /// </summary>
    public static async Task<int> ShowAsync(Context context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var held = await context.Dns.ReadAsync(ct).ConfigureAwait(false);
        Terminal.Say($"answering    {Switch(held.IsEnabled)}");
        Terminal.Say($"port         {held.Port.ToString(CultureInfo.InvariantCulture)}");
        Terminal.Say($"asks         {string.Join(", ", held.Upstreams)}");
        Terminal.Say($"encrypted    {Encrypted(held)}");
        Terminal.Say($"through      {(held.Outbound.Length == 0 ? "the way out of the host" : held.Outbound)}");

        return 0;
    }

    /// <summary>
    /// Prints the settings of the resolver it is asked for, one line each, for a script to read.
    /// </summary>
    public static async Task<int> GetAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        var held = await context.Dns.ReadAsync(ct).ConfigureAwait(false);
        var names = args.Positional.Skip(2).ToList();
        var values = names.Select(name => Setting(held, name)).ToList();
        if (names.Count == 0 || values.Any(value => value is null))
        {
            return Refuse("dns get enabled | port | upstreams | encrypted | outbound ...");
        }

        foreach (var value in values)
        {
            Terminal.Say(value!);
        }

        return 0;
    }

    /// <summary>
    /// Turns the resolver on or off or names its name servers, and has the running panel take the change.
    /// </summary>
    public static async Task<int> SetAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        var held = await context.Dns.ReadAsync(ct).ConfigureAwait(false);
        if (Draft(held, args) is not { } draft)
        {
            return Refuse($"dns set --enabled on | off [--upstreams <servers> | {Usual}]");
        }

        var saved = await context.Dns.SaveAsync(draft, ct).ConfigureAwait(false);
        if (!saved.IsOk || saved.Record is null)
        {
            return Refuse(saved.Message);
        }

        Terminal.Say($"saved: the resolver is {Switch(saved.Record.IsEnabled)} and asks {string.Join(", ", saved.Record.Upstreams)}");

        return await TakeAsync(context, ct).ConfigureAwait(false);
    }

    // Returns the settings a command asks for, null when it names nothing to change or names it wrong.
    private static DnsSettings? Draft(DnsSettings held, Arguments args)
    {
        var enabled = args.Value("enabled") is { } word ? PanelEdit.SwitchOf(word) : held.IsEnabled;
        var upstreams = args.Value("upstreams") is { } list ? Servers(list) : held.Upstreams;
        if (enabled is null || !(args.Has("enabled") || args.Has("upstreams")))
        {
            return null;
        }

        return held with { IsEnabled = enabled.Value, Upstreams = upstreams };
    }

    private static IReadOnlyList<string> Servers(string list) =>
        string.Equals(list.Trim(), Usual, StringComparison.OrdinalIgnoreCase)
            ? DnsDefaults.Upstreams
            : list.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Has the running panel start the resolver over on what was saved; a panel that is down takes it at its start.
    private static async Task<int> TakeAsync(Context context, CancellationToken ct)
    {
        var code = await PanelCall.RunAsync(
            context,
            async call =>
            {
                var (status, body) = await call.SendAsync(HttpMethod.Post, "api/dns/restart", null, ct).ConfigureAwait(false);
                if (status != 200)
                {
                    return PanelCall.Refused(status, body);
                }

                Terminal.Say(Running(body));

                return 0;
            },
            ct).ConfigureAwait(false);
        if (code != PanelCall.Silent)
        {
            return code;
        }

        Terminal.Say("the panel is not running: it takes the change when it starts");

        return 0;
    }

    // Returns what the panel made of the change, in a line.
    private static string Running(JsonElement body)
    {
        var on = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("isEnabled", out var enabled)
            && enabled.ValueKind == JsonValueKind.True;
        if (!on)
        {
            return "the panel stopped the resolver";
        }

        var state = body.TryGetProperty("state", out var found) ? found : default;
        var running = state.ValueKind == JsonValueKind.Object && state.TryGetProperty("isRunning", out var runs)
            && runs.ValueKind == JsonValueKind.True;
        if (!running)
        {
            return $"the resolver does not run yet: {PanelCall.Text(state, "fault")}";
        }

        var listening = state.TryGetProperty("listening", out var taken) && taken.ValueKind == JsonValueKind.Array
            ? string.Join(", ", taken.EnumerateArray().Select(one => one.GetString()))
            : string.Empty;

        return $"the panel answers the clients on {listening} port {PanelCall.Number(body, "port").ToString(CultureInfo.InvariantCulture)}";
    }

    // Returns one setting as a script reads it, null for a name the resolver does not hold.
    private static string? Setting(DnsSettings held, string name) => name switch
    {
        "enabled" => Switch(held.IsEnabled),
        "port" => held.Port.ToString(CultureInfo.InvariantCulture),
        "upstreams" => string.Join(",", held.Upstreams),
        "encrypted" => Encrypted(held),
        "outbound" => held.Outbound,
        _ => null,
    };

    // Tells whether the questions leave the host encrypted: all, none or partly.
    private static string Encrypted(DnsSettings held)
    {
        var encrypted = held.Upstreams.Count(one => DnsRules.Server(one, out var server) && server.IsEncrypted);
        if (encrypted == held.Upstreams.Count)
        {
            return "all";
        }

        return encrypted == 0 ? "none" : "partly";
    }

    private static string Switch(bool on) => on ? "on" : "off";

    private static int Refuse(string message)
    {
        Terminal.Fail(message);

        return 2;
    }

    private static int Usage() => Refuse(
        $"usage: amneziageo-server dns show | get <name>... | set --enabled on | off [--upstreams <servers> | {Usual}]");
}
