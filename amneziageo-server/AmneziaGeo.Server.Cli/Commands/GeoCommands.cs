using System.Globalization;
using System.Text.Json;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that refresh the geo sources through the panel itself.
/// </summary>
public static class GeoCommands
{
    /// <summary>
    /// Runs one geo command and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        return args.At(1) switch
        {
            "update" => await UpdateAsync(context, ct).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Downloads every geo source of the panel now and prints what each one brought.
    /// </summary>
    public static Task<int> UpdateAsync(Context context, CancellationToken ct) =>
        PanelCall.RunAsync(context, async call =>
        {
            Terminal.Say("the panel downloads its geo sources");
            var (status, body) = await call.SendAsync(HttpMethod.Post, "api/geo/update", null, ct).ConfigureAwait(false);
            if (status != 200)
            {
                return PanelCall.Refused(status, body);
            }

            if (body.ValueKind != JsonValueKind.Array || body.GetArrayLength() == 0)
            {
                Terminal.Say("the panel carries no geo sources");

                return 0;
            }

            var failed = 0;
            foreach (var source in body.EnumerateArray())
            {
                var name = PanelCall.Text(source, "name");
                if (PanelCall.Text(source, "lastError") is { Length: > 0 } error)
                {
                    failed++;
                    Terminal.Say($"{name}: {error}");

                    continue;
                }

                var entries = PanelCall.Number(source, "entryCount").ToString(CultureInfo.InvariantCulture);
                var size = PanelCall.Number(source, "size").ToString(CultureInfo.InvariantCulture);
                Terminal.Say($"{name}: {entries} entries, {size} bytes, updated {PanelCall.Text(source, "updatedUtc")}");
            }

            return failed == 0 ? 0 : 1;
        }, ct);

    private static int Usage()
    {
        Terminal.Fail("usage: geo update");

        return 2;
    }
}
