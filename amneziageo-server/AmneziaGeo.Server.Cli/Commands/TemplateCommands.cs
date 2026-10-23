using System.Globalization;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that read the client templates and keep their clients to the routing presets they hand out.
/// </summary>
public static class TemplateCommands
{
    /// <summary>
    /// Runs one template command and returns the exit code.
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
    /// Prints every template with the number of its clients, the routing presets it hands out and its marks.
    /// </summary>
    public static async Task<int> ListAsync(Context context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var templates = await context.Templates.ListAsync(ct).ConfigureAwait(false);
        var uses = await context.Templates.UsesAsync(ct).ConfigureAwait(false);
        var presets = (await context.Presets.ListAsync(ct).ConfigureAwait(false)).ToDictionary(preset => preset.Id, preset => preset.Name);
        foreach (var template in templates)
        {
            Terminal.Say($"{template.Name}: {Line(template, uses.GetValueOrDefault(template.Id), presets)}");
        }

        return 0;
    }

    /// <summary>
    /// Keeps the clients of a template to its routing presets or lets them route by lists of their own.
    /// </summary>
    public static async Task<int> SetAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.At(2) is not { } name || args.Value("presets-only") is not { } value || PanelEdit.SwitchOf(value) is not { } only)
        {
            return Refuse("template set <name> --presets-only on | off");
        }

        var templates = await context.Templates.ListAsync(ct).ConfigureAwait(false);
        if (templates.FirstOrDefault(one => string.Equals(one.Name, name, StringComparison.Ordinal)) is not { } held)
        {
            return Refuse($"there is no template called '{name}'");
        }

        var saved = await context.Templates.ChangeAsync(held.Id, held with { PresetsOnly = only }, ct).ConfigureAwait(false);
        if (!saved.IsOk)
        {
            return Refuse(saved.Message);
        }

        Terminal.Say(saved.Record!.PresetsOnly
            ? $"{saved.Record.Name}: the applications of its clients route its configurations by its routing presets alone"
            : $"{saved.Record.Name}: the applications of its clients may route by lists of their own");

        return 0;
    }

    // Returns the clients, the presets and the marks of a template, the way the list prints them.
    private static string Line(ClientTemplate template, int clients, IReadOnlyDictionary<long, string> presets)
    {
        var named = template.Presets.Where(presets.ContainsKey).Select(id => presets[id]).ToList();
        var line = $"clients {clients.ToString(CultureInfo.InvariantCulture)}, routing presets {named.Count.ToString(CultureInfo.InvariantCulture)}";
        if (named.Count > 0)
        {
            line += $" ({string.Join(", ", named)})";
        }

        return template.PresetsOnly ? line + ", presets only" : line;
    }

    private static int Refuse(string message)
    {
        Terminal.Fail(message);

        return 2;
    }

    private static int Usage() => Refuse("usage: amneziageo-server template list | set <name> --presets-only on | off");
}
