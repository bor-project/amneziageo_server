using System.Globalization;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that read the routing presets and keep them read-only in the applications of the clients.
/// </summary>
public static class PresetCommands
{
    /// <summary>
    /// Runs one preset command and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        return args.At(1) switch
        {
            "list" => await ListAsync(context, ct).ConfigureAwait(false),
            "set" => await SetAsync(context, args, ct).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Prints every routing preset with the number of its entries, of the templates that hand it out and its marks.
    /// </summary>
    public static async Task<int> ListAsync(Context context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var presets = await context.Presets.ListAsync(ct).ConfigureAwait(false);
        var uses = await context.Presets.UsesAsync(ct).ConfigureAwait(false);
        if (presets.Count == 0)
        {
            Terminal.Say("the panel holds no routing presets");

            return 0;
        }

        foreach (var preset in presets)
        {
            Terminal.Say($"{preset.Name}: {Line(preset, uses.GetValueOrDefault(preset.Id))}");
        }

        return 0;
    }

    /// <summary>
    /// Keeps a routing preset read-only in the applications of the clients or lets them change it.
    /// </summary>
    public static async Task<int> SetAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.At(2) is not { } name || args.Value("readonly") is not { } value || PanelEdit.SwitchOf(value) is not { } readOnly)
        {
            return Refuse("preset set <name> --readonly on | off");
        }

        var presets = await context.Presets.ListAsync(ct).ConfigureAwait(false);
        if (presets.FirstOrDefault(one => string.Equals(one.Name, name, StringComparison.Ordinal)) is not { } held)
        {
            return Refuse($"there is no routing preset called '{name}'");
        }

        var saved = await context.Presets.ChangeAsync(held.Id, held with { ReadOnly = readOnly }, ct).ConfigureAwait(false);
        if (!saved.IsOk)
        {
            return Refuse(saved.Message);
        }

        Terminal.Say(saved.Record!.ReadOnly
            ? $"{saved.Record.Name}: the applications of the clients keep the list read-only and take its versions as they come"
            : $"{saved.Record.Name}: the applications of the clients may change the list");

        return 0;
    }

    // Returns what a preset carries and its marks, the way the list prints them.
    private static string Line(RoutingPreset preset, int templates)
    {
        var marks = new List<string>
        {
            $"through the tunnel {Count(preset.Proxy.Count)}",
            $"directly {Count(preset.Direct.Count)}",
            $"blocked {Count(preset.Block.Count)}",
            $"templates {Count(templates)}",
        };
        if (preset.IsDefault)
        {
            marks.Add("on by default");
        }

        if (preset.ReadOnly)
        {
            marks.Add("read-only");
        }

        if (preset.Full)
        {
            marks.Add("everything through the tunnel: unsafe for the server, turn it off in the panel");
        }

        return string.Join(", ", marks);
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Refuse(string message)
    {
        Terminal.Fail(message);

        return 2;
    }

    private static int Usage() => Refuse("usage: amneziageo-server preset list | set <name> --readonly on | off");
}
