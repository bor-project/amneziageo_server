using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// Moves the panel of a compose project onto the image of a release, and back where it does not come up healthy.
/// </summary>
public static partial class HandoverCommand
{
    /// <summary>
    /// The word that starts the tool instead of the panel.
    /// </summary>
    public const string Name = "handover";

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan Stopping = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Step = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Settling = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs the move and returns what the shell takes as its exit code.
    /// </summary>
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        try
        {
            var move = new Handover(
                Told("PROJECT"),
                Told("SERVICE"),
                Told("DIR"),
                Told("IMAGE"),
                Told("FROM"),
                Told("TAG"),
                Told("DATA"));
            using var engine = new DockerEngine(DockerUpdater.InnerSocket);

            return await MoveAsync(engine, move, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Say(ex.Message);

            return 1;
        }
    }

    /// <summary>
    /// Returns the text of an env file with the line of the tag in it.
    /// </summary>
    public static string Tagged(string? held, string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (string.IsNullOrEmpty(held))
        {
            return line + "\n";
        }

        return Tags().IsMatch(held) ? Tags().Replace(held, line) : held.TrimEnd('\n') + "\n" + line + "\n";
    }

    public static async Task<int> MoveAsync(DockerEngine engine, Handover move, CancellationToken ct)
    {
        Say($"moving {move.Service} of {move.Project} from {move.From} to {move.Image}");
        var id = await FoundAsync(engine, move, ct).ConfigureAwait(false);
        var container = await engine.ContainerAsync(id, ct).ConfigureAwait(false);
        var name = DockerEngine.Text(container["Name"]).TrimStart('/');
        var image = DockerEngine.Text(container["Config"]?["Image"]);
        var build = DockerEngine.Text(container["Image"]);
        if (name.Length == 0 || image.Length == 0 || build.Length == 0)
        {
            throw new InvalidOperationException($"the daemon did not identify container {id} and its image build");
        }
        var baseline = await engine.ImageAsync(build, ct).ConfigureAwait(false);
        var spare = name + "-" + move.From;
        await FreeAsync(engine, spare, ct).ConfigureAwait(false);
        var backup = string.Empty;
        var env = default(string);
        var tagged = false;
        var stopped = false;
        var renamed = false;
        var made = string.Empty;
        var fault = string.Empty;
        var cancellation = default(OperationCanceledException);
        try
        {
            Say($"stopping {name}");
            await engine.StopAsync(id, Stopping, ct).ConfigureAwait(false);
            stopped = true;
            backup = ReleaseDatabases.Save(move.Data, "docker:" + build);
            env = Tag(move.Folder, move.To);
            tagged = true;
            await engine.RenameAsync(id, spare, ct).ConfigureAwait(false);
            renamed = true;
            Say($"starting {name} on {move.Image}");
            made = await engine.CreateAsync(name, DockerSwap.Spec(container, baseline, move.Image), ct).ConfigureAwait(false);
            await engine.StartAsync(made, ct).ConfigureAwait(false);
            var state = await WaitAsync(engine, made, ct).ConfigureAwait(false);
            fault = state is "healthy" or "running" ? string.Empty : $"the container of {move.To} is {state}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            fault = ex.Message;
            cancellation = ex as OperationCanceledException;
        }
        if (fault.Length == 0)
        {
            var target = DockerEngine.Text((await engine.ContainerAsync(made, ct).ConfigureAwait(false))["Image"]);
            await Quietly(() => engine.RemoveAsync(id, ct)).ConfigureAwait(false);
            ReleaseDatabases.Prune(move.Data, "docker:" + build, "docker:" + target);
            Say($"{move.Service} runs {move.To}");
            return 0;
        }
        Say($"{move.To} did not come up: {fault}");
        if (made.Length > 0)
        {
            await Quietly(async () => Console.Write(await engine.LogsAsync(made, 40, CancellationToken.None).ConfigureAwait(false))).ConfigureAwait(false);
            await engine.StopAsync(made, Stopping, CancellationToken.None).ConfigureAwait(false);
            await engine.RemoveAsync(made, CancellationToken.None).ConfigureAwait(false);
        }
        if (stopped)
        {
            ReleaseDatabases.Restore(move.Data, backup);
            if (tagged)
            {
                Untag(move.Folder, env);
            }
            if (renamed)
            {
                await engine.RenameAsync(id, name, CancellationToken.None).ConfigureAwait(false);
            }
            await engine.StartAsync(id, CancellationToken.None).ConfigureAwait(false);
        }
        if (cancellation is not null)
        {
            throw cancellation;
        }
        return 1;
    }

    private static async Task<string> FoundAsync(DockerEngine engine, Handover move, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("AMNEZIAGEO_UPDATE_CONTAINER") is { Length: > 0 } told)
        {
            return told;
        }

        var found = await engine
            .FoundAsync([$"com.docker.compose.project={move.Project}", $"com.docker.compose.service={move.Service}"], ct)
            .ConfigureAwait(false);

        return found.Count > 0
            ? DockerEngine.Text(found[0]["Id"])
            : throw new InvalidOperationException($"the daemon holds no container of {move.Service} of {move.Project}");
    }

    private static async Task FreeAsync(DockerEngine engine, string name, CancellationToken ct)
    {
        if (await engine.HeldAsync(name, ct).ConfigureAwait(false) is not { } held)
        {
            return;
        }

        if (DockerEngine.Text(held["State"]?["Status"]) == "running")
        {
            throw new InvalidOperationException($"{name} of an earlier update still runs");
        }

        Say($"removing {name} of an earlier update");
        await engine.RemoveAsync(name, ct).ConfigureAwait(false);
    }

    private static async Task<string> WaitAsync(DockerEngine engine, string id, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        var state = string.Empty;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var told = (await engine.ContainerAsync(id, ct).ConfigureAwait(false))["State"];
            var health = DockerEngine.Text(told?["Health"]?["Status"]);
            state = health.Length > 0 ? health : DockerEngine.Text(told?["Status"]);
            if (state is "healthy" or "unhealthy" or "exited" or "dead")
            {
                return state;
            }

            if (health.Length == 0 && state == "running" && Started(told) + Settling < DateTimeOffset.UtcNow)
            {
                return state;
            }

            await Task.Delay(Step, ct).ConfigureAwait(false);
        }

        return state;
    }

    private static DateTimeOffset Started(JsonNode? state) =>
        DateTimeOffset.TryParse(DockerEngine.Text(state?["StartedAt"]), CultureInfo.InvariantCulture, out var started)
            ? started
            : DateTimeOffset.UtcNow;

    private static string? Tag(string folder, string to)
    {
        var path = Path.Combine(folder, ".env");
        var held = File.Exists(path) ? File.ReadAllText(path) : null;
        var stage = path + "." + Guid.NewGuid().ToString("N");
        File.WriteAllText(stage, Tagged(held, "AMNEZIAGEO_TAG=" + to));
        File.Move(stage, path, overwrite: true);
        Say($"{path} names {to}");

        return held;
    }

    private static void Untag(string folder, string? held)
    {
        var path = Path.Combine(folder, ".env");
        if (held is null)
        {
            File.Delete(path);
        }
        else
        {
            File.WriteAllText(path, held);
        }
    }

    private static async Task Quietly(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Say(ex.Message);
        }
    }

    private static string Told(string name)
    {
        var told = Environment.GetEnvironmentVariable("AMNEZIAGEO_UPDATE_" + name) ?? string.Empty;

        return told.Length > 0 ? told : throw new InvalidOperationException($"AMNEZIAGEO_UPDATE_{name} is not set");
    }

    private static void Say(string line) =>
        Console.WriteLine($"{DateTimeOffset.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {line}");

    [GeneratedRegex("(?m)^AMNEZIAGEO_TAG=.*$")]
    private static partial Regex Tags();

    public sealed record Handover(
        string Project,
        string Service,
        string Folder,
        string Image,
        string From,
        string To,
        string Data);
}
