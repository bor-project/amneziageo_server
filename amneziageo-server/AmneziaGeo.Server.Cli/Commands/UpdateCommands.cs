using System.Text.Json;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that move the panel to another release through the panel itself.
/// </summary>
public static class UpdateCommands
{
    /// <summary>
    /// The exit code when the channel holds nothing newer.
    /// </summary>
    public const int NothingNewer = 3;

    /// <summary>
    /// Runs one update command and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        return args.At(1) switch
        {
            "status" => await StatusAsync(context, ct).ConfigureAwait(false),
            "check" => await CheckAsync(context, ct).ConfigureAwait(false),
            "apply" => await ApplyAsync(context, args, ct).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Prints what the panel knows of its releases and of the update started last.
    /// </summary>
    public static Task<int> StatusAsync(Context context, CancellationToken ct) =>
        PanelCall.RunAsync(context, async call =>
        {
            var (status, body) = await call.SendAsync(HttpMethod.Get, "api/update", null, ct).ConfigureAwait(false);
            if (status != 200)
            {
                return PanelCall.Refused(status, body);
            }

            Print(body);

            return 0;
        }, ct);

    /// <summary>
    /// Asks the panel to look over its releases and prints what it found.
    /// </summary>
    public static Task<int> CheckAsync(Context context, CancellationToken ct) =>
        PanelCall.RunAsync(context, async call =>
        {
            var (status, body) = await call.SendAsync(HttpMethod.Post, "api/update/check", null, ct).ConfigureAwait(false);
            if (status != 200)
            {
                return PanelCall.Refused(status, body);
            }

            Print(body);

            return 0;
        }, ct);

    /// <summary>
    /// Moves the panel to the newest release of its channel, to the test channel first when asked for a beta.
    /// </summary>
    public static async Task<int> ApplyAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Has("beta"))
        {
            var held = await context.Panel.ReadAsync(ct).ConfigureAwait(false);
            if (!held.Prereleases)
            {
                var saved = await context.Panel.SaveAsync(held with { Prereleases = true }, ct).ConfigureAwait(false);
                if (!saved.IsOk)
                {
                    Terminal.Fail(saved.Message);

                    return 1;
                }

                Terminal.Say($"the panel takes releases from the {PanelEdit.Test} channel now");
            }
        }

        return await PanelCall.RunAsync(context, async call =>
        {
            var (status, body) = await call.SendAsync(HttpMethod.Post, "api/update/check", null, ct).ConfigureAwait(false);
            if (status != 200)
            {
                return PanelCall.Refused(status, body);
            }

            var version = args.Value("version") ?? Latest(body);
            if (version is null && Fault(body) is { } fault)
            {
                Terminal.Fail($"the panel could not reach its releases: {fault}");

                return 1;
            }

            if (version is null)
            {
                Terminal.Say($"there is no release newer than {PanelCall.Text(body, "current")} on the {PanelCall.Text(body, "channel")} channel");

                return NothingNewer;
            }

            (status, body) = await call.SendAsync(HttpMethod.Post, "api/update/apply", new { version }, ct).ConfigureAwait(false);
            if (status != 202)
            {
                return PanelCall.Refused(status, body);
            }

            Terminal.Say($"the panel moves to {version}");

            return 0;
        }, ct).ConfigureAwait(false);
    }

    // Writes the state of the releases the panel reported.
    private static void Print(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        Terminal.Say($"current  {PanelCall.Text(body, "current")}, {PanelCall.Text(body, "mode")}, {PanelCall.Text(body, "channel")} channel");
        var fault = Fault(body);
        Terminal.Say($"latest   {Latest(body) ?? (fault is null ? "nothing newer" : "unknown")}");
        Terminal.Say($"stage    {PanelCall.Text(body, "stage")}");
        if (PanelCall.Text(body, "blocker") is { Length: > 0 } blocker)
        {
            Terminal.Say($"blocked  {blocker}");
        }

        if (fault is not null)
        {
            Terminal.Say($"fault    {fault}");
        }

        if (!body.TryGetProperty("run", out var run) || run.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        Terminal.Say($"run      {PanelCall.Text(run, "from")} -> {PanelCall.Text(run, "to")}: {PanelCall.Text(run, "state")}");
        if (run.TryGetProperty("log", out var log) && log.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in log.EnumerateArray().TakeLast(12))
            {
                Terminal.Say("  " + line.GetString());
            }
        }
    }

    // Returns the release newer than the panel, null when there is none.
    private static string? Latest(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty("latest", out var latest)
        && PanelCall.Text(latest, "version") is { Length: > 0 } version
            ? version
            : null;

    // Returns why the last look at the releases failed, null when it did not.
    private static string? Fault(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty("fault", out var fault)
        && fault.ValueKind == JsonValueKind.Object
            ? PanelCall.Text(fault, "message")
            : null;

    private static int Usage()
    {
        Terminal.Fail("usage: update status | check | apply [--beta] [--version <version>]");

        return 2;
    }
}
