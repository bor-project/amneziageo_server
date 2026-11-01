using System.Globalization;

using AmneziaGeo.Server.Dal.Exchange;
using AmneziaGeo.Server.Routing.Exchange;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that carry the routing of the panel to a file and take a file back in.
/// </summary>
public static class RoutingCommands
{
    /// <summary>
    /// Runs one routing command and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        return args.At(1) switch
        {
            "export" => await ExportAsync(context, args, ct).ConfigureAwait(false),
            "import" => await ImportAsync(context, args, ct).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Writes the routing of the panel to a file: the rules, the basic lists, the presets and the addresses of the
    /// geo sources.
    /// </summary>
    public static async Task<int> ExportAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.At(2) is not { } file || file.Length == 0)
        {
            return Refuse("usage: amneziageo-server routing export <file>");
        }

        var doc = await RoutingExport.TakeAsync(context.Rules, context.Presets, context.Geo, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(file, RoutingExport.Text(doc), ct).ConfigureAwait(false);

        Terminal.Say(
            $"the routing of the panel left for {file}: {Count(doc.Rules.Count)} rules, " +
            $"{Count(doc.Basic.Direct.Count + doc.Basic.Block.Count)} basic entries, " +
            $"{Count(doc.Presets.Count)} presets, {Count(doc.Sources.Count)} geo sources");

        return 0;
    }

    /// <summary>
    /// Reads a routing file and prints what it would add and replace; with --apply lays it over the routing of the
    /// panel.
    /// </summary>
    public static async Task<int> ImportAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.At(2) is not { } file || file.Length == 0)
        {
            return Refuse("usage: amneziageo-server routing import <file> [--apply]");
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return Refuse($"the file {file} does not read");
        }

        var read = RoutingFile.Read(text);
        if (!read.IsOk)
        {
            return Refuse(read.Message);
        }

        var plan = await RoutingImport
            .PlanAsync(read.Doc!, context.Rules, context.Presets, context.Geo, context.Outbounds, context.Clients, ct)
            .ConfigureAwait(false);
        Plan(plan);

        if (!args.Has("apply"))
        {
            Terminal.Say("nothing was written; pass --apply to lay this import over the routing of the panel");

            return 0;
        }

        var answer = await RoutingImport.ApplyAsync(plan, context.Rules, context.Presets, context.Geo, ct).ConfigureAwait(false);
        Lines("taken in", answer.Added);
        Lines("replaced", answer.Replaced);
        if (answer.BasicReplaced)
        {
            Terminal.Say("the basic lists were laid over");
        }

        Lines("refused", answer.Refused);
        Lines("off the host yet", answer.Detached);

        return answer.Refused.Count > 0 ? 2 : 0;
    }

    private static void Plan(RoutingPlan plan)
    {
        Lines("added", plan.NewRules.Select(name => $"rule '{name}'")
            .Concat(plan.NewPresets.Select(name => $"preset '{name}'"))
            .Concat(plan.NewSources.Select(name => $"source '{name}'")));
        Lines("replaced", plan.ReplacedRules.Select(name => $"rule '{name}'")
            .Concat(plan.ReplacedPresets.Select(name => $"preset '{name}'"))
            .Concat(plan.ReplacedSources.Select(name => $"source '{name}'")));
        if (plan.ReplacesBasic)
        {
            Terminal.Say("the basic lists would be laid over");
        }

        Lines("off the host yet", plan.Detached);
    }

    private static void Lines(string head, IEnumerable<string> lines)
    {
        var held = lines.ToList();
        if (held.Count == 0)
        {
            return;
        }

        Terminal.Say($"{head}:");
        foreach (var line in held)
        {
            Terminal.Say($"  {line}");
        }
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Refuse(string message)
    {
        Terminal.Fail(message);

        return 2;
    }

    private static int Usage() => Refuse("usage: amneziageo-server routing export <file> | import <file> [--apply]");
}
