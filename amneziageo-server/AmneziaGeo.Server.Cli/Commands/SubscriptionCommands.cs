using System.Globalization;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that read the subscriptions and hold their port open in the firewall.
/// </summary>
public static class SubscriptionCommands
{
    /// <summary>
    /// Runs one subscriptions command and returns the exit code.
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
    /// Prints where the subscriptions are served and whether their port is held open.
    /// </summary>
    public static async Task<int> ShowAsync(Context context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var held = await context.Subscriptions.ReadAsync(ct).ConfigureAwait(false);
        Terminal.Say($"served       {(held.IsEnabled ? "on" : "off")}");
        Terminal.Say($"port         {(held.Separate ? held.Port.ToString(CultureInfo.InvariantCulture) : "the port of the services of each endpoint")}");
        if (held.Separate)
        {
            Terminal.Say($"listen       {(held.Listen.Count == 0 ? "every address" : string.Join(", ", held.Listen))}");
            Terminal.Say($"firewall     {(held.Opened ? "the port is held open" : "the port is left as it is")}");
        }

        return 0;
    }

    /// <summary>
    /// Prints the settings of the subscriptions it is asked for, one line each, for a script to read.
    /// </summary>
    public static async Task<int> GetAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        var held = await context.Subscriptions.ReadAsync(ct).ConfigureAwait(false);
        var names = args.Positional.Skip(2).ToList();
        var values = names.Select(name => Setting(held, name)).ToList();
        if (names.Count == 0 || values.Any(value => value is null))
        {
            return Refuse("subscriptions get served | separate | port | opened ...");
        }

        foreach (var value in values)
        {
            Terminal.Say(value!);
        }

        return 0;
    }

    /// <summary>
    /// Holds the port of the subscriptions open in the firewall or stops holding it.
    /// </summary>
    public static async Task<int> SetAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Value("opened") is not { } opened || PanelEdit.SwitchOf(opened) is not { } open)
        {
            return Refuse("subscriptions set --opened on | off");
        }

        var held = await context.Subscriptions.ReadAsync(ct).ConfigureAwait(false);
        if (!held.Separate)
        {
            return Refuse("the subscriptions answer on the ports of the services of the endpoints, which the endpoints hold open");
        }

        var saved = await context.Subscriptions.SaveAsync(held with { Opened = open }, ct).ConfigureAwait(false);
        Terminal.Say($"saved: the port {saved.Port.ToString(CultureInfo.InvariantCulture)} of the subscriptions is {(saved.Opened ? "held open" : "left as it is")} once the panel starts over");

        return 0;
    }

    // Returns one setting as a script reads it, null for a name the subscriptions do not hold.
    private static string? Setting(SubscriptionSettings held, string name) => name switch
    {
        "served" => held.IsEnabled ? "on" : "off",
        "separate" => held.Separate ? "on" : "off",
        "port" => held.Port.ToString(CultureInfo.InvariantCulture),
        "opened" => held.Opened ? "on" : "off",
        _ => null,
    };

    private static int Refuse(string message)
    {
        Terminal.Fail(message);

        return 2;
    }

    private static int Usage() => Refuse("usage: amneziageo-server subscriptions show | get <name>... | set --opened on | off");
}
