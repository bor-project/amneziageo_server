using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// Where the panel runs in its compose project, as the daemon tells it.
/// </summary>
/// <param name="Container">The container of the panel.</param>
/// <param name="Image">The image the container runs.</param>
/// <param name="Project">The compose project of the container.</param>
/// <param name="Folder">The directory of the project on the host.</param>
/// <param name="Files">The compose files of the project.</param>
/// <param name="Service">The service of the panel in the project.</param>
/// <param name="Data">The volume or the directory of the host the data of the panel lies in.</param>
/// <param name="Socket">The socket of the daemon on the host.</param>
public sealed record DockerPlace(
    string Container,
    string Image,
    string Project,
    string Folder,
    IReadOnlyList<string> Files,
    string Service,
    string Data,
    string Socket);

/// <summary>
/// What the tool that moves the panel onto a release is told.
/// </summary>
/// <param name="Image">The image the panel moves to.</param>
/// <param name="From">The version the panel runs.</param>
/// <param name="To">The version the panel moves to.</param>
/// <param name="Data">The directory the data of the panel lies in, inside the container.</param>
/// <param name="Work">The directory the log and the exit code of the update are written to.</param>
public sealed record DockerHandover(string Image, string From, string To, string Data, string Work);

/// <summary>
/// Moves a panel that runs in a compose project onto the image of a release.
/// </summary>
public static partial class DockerUpdater
{
    /// <summary>
    /// The label the containers that move the panel carry.
    /// </summary>
    public const string Mark = "amneziageo.update";

    /// <summary>
    /// The tool in the image that moves the panel.
    /// </summary>
    public const string Tool = "/usr/local/bin/amneziageo-server-update";

    /// <summary>
    /// Where the socket of the daemon lies in the container that moves the panel.
    /// </summary>
    public const string InnerSocket = "/var/run/docker.sock";

    private static readonly TimeSpan PullLimit = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Finds where the panel runs, or tells why it cannot move itself.
    /// </summary>
    public static async Task<(DockerPlace? Place, string Blocker)> FindAsync(string socket, string data, CancellationToken ct)
    {
        if (!File.Exists(socket))
        {
            return (null, "no-docker");
        }

        var container = ContainerOf(await File.ReadAllTextAsync("/proc/self/mountinfo", ct).ConfigureAwait(false));
        if (container is null)
        {
            return (null, "no-container");
        }

        try
        {
            using var engine = new DockerEngine(socket);
            var inspect = await engine.ContainerAsync(container, ct).ConfigureAwait(false);

            return Place(inspect, data, socket);
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (null, "no-docker");
        }
    }

    /// <summary>
    /// Returns the id of the container the mounts of a process belong to, or null outside a container.
    /// </summary>
    public static string? ContainerOf(string mountinfo)
    {
        var match = ContainerDirectory().Match(mountinfo ?? string.Empty);

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Reads where the panel runs out of what the daemon tells of its container, or why it cannot move itself.
    /// </summary>
    public static (DockerPlace? Place, string Blocker) Place(JsonNode inspect, string data, string socket)
    {
        ArgumentNullException.ThrowIfNull(inspect);

        var labels = inspect["Config"]?["Labels"];
        var project = DockerEngine.Text(labels?["com.docker.compose.project"]);
        var folder = DockerEngine.Text(labels?["com.docker.compose.project.working_dir"]).TrimEnd('/');
        var service = DockerEngine.Text(labels?["com.docker.compose.service"]);
        var files = DockerEngine.Text(labels?["com.docker.compose.project.config_files"])
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (project.Length == 0 || folder.Length == 0 || service.Length == 0 || files.Length == 0)
        {
            return (null, "no-compose");
        }

        if (files.Any(file => !file.StartsWith(folder + "/", StringComparison.Ordinal)))
        {
            return (null, "compose-elsewhere");
        }

        var held = Mount(inspect, data);
        if (held.Length == 0)
        {
            return (null, "no-data");
        }

        var bus = Mount(inspect, socket);
        if (bus.Length == 0)
        {
            return (null, "no-docker");
        }

        var place = new DockerPlace(
            DockerEngine.Text(inspect["Id"]),
            DockerEngine.Text(inspect["Config"]?["Image"]),
            project,
            folder,
            files,
            service,
            held,
            bus);

        return (place, string.Empty);
    }

    /// <summary>
    /// Returns the name of an image without its tag and digest.
    /// </summary>
    public static string Repository(string image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var bare = image.Split('@')[0];
        var slash = bare.LastIndexOf('/');
        var colon = bare.LastIndexOf(':');

        return colon > slash ? bare[..colon] : bare;
    }

    /// <summary>
    /// Returns the container that moves the panel onto a release.
    /// </summary>
    public static JsonObject Spec(DockerPlace place, DockerHandover handover)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(handover);

        return new JsonObject
        {
            ["Image"] = handover.Image,
            ["Entrypoint"] = new JsonArray(Tool),
            ["Cmd"] = new JsonArray(),
            ["Env"] = new JsonArray(
                "AMNEZIAGEO_UPDATE_CONTAINER=" + place.Container,
                "AMNEZIAGEO_UPDATE_PROJECT=" + place.Project,
                "AMNEZIAGEO_UPDATE_DIR=" + place.Folder,
                "AMNEZIAGEO_UPDATE_FILES=" + string.Join(',', place.Files),
                "AMNEZIAGEO_UPDATE_SERVICE=" + place.Service,
                "AMNEZIAGEO_UPDATE_IMAGE=" + handover.Image,
                "AMNEZIAGEO_UPDATE_FROM=" + handover.From,
                "AMNEZIAGEO_UPDATE_TAG=" + handover.To,
                "AMNEZIAGEO_UPDATE_DATA=" + handover.Data,
                "AMNEZIAGEO_UPDATE_WORK=" + handover.Work),
            ["Labels"] = new JsonObject { [Mark] = handover.To },
            ["Healthcheck"] = new JsonObject { ["Test"] = new JsonArray("NONE") },
            ["HostConfig"] = new JsonObject
            {
                ["Binds"] = new JsonArray(
                    place.Socket + ":" + InnerSocket,
                    place.Folder + ":" + place.Folder,
                    place.Data + ":" + handover.Data),
                ["NetworkMode"] = "none",
            },
        };
    }

    /// <summary>
    /// Pulls the image of a release and names it the way the compose project names the image of the panel.
    /// </summary>
    public static async Task<string> PullAsync(string socket, DockerPlace place, UpdateManifest manifest, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Image.Length == 0)
        {
            throw new InvalidDataException($"the release {manifest.Version} carries no image");
        }

        var repository = Repository(place.Image);
        var tag = manifest.Version.ToString();
        using var engine = new DockerEngine(socket);
        await engine.PullAsync(manifest.Image, PullLimit, ct).ConfigureAwait(false);
        await engine.TagAsync(manifest.Image, repository, tag, ct).ConfigureAwait(false);

        return repository + ":" + tag;
    }

    /// <summary>
    /// Starts the container that moves the panel onto a release; it stops the panel and starts it over.
    /// </summary>
    public static async Task StartAsync(string socket, DockerPlace place, DockerHandover handover, CancellationToken ct)
    {
        using var engine = new DockerEngine(socket);
        await SweepAsync(engine, ct).ConfigureAwait(false);
        var id = await engine.CreateAsync("amneziageo-server-update-" + handover.To, Spec(place, handover), ct)
            .ConfigureAwait(false);
        await engine.StartAsync(id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the containers that moved the panel and ended.
    /// </summary>
    public static async Task SweepAsync(string socket, CancellationToken ct)
    {
        using var engine = new DockerEngine(socket);
        await SweepAsync(engine, ct).ConfigureAwait(false);
    }

    private static async Task SweepAsync(DockerEngine engine, CancellationToken ct)
    {
        foreach (var (id, state) in await engine.ListAsync(Mark, ct).ConfigureAwait(false))
        {
            if (state is "exited" or "dead")
            {
                await engine.RemoveAsync(id, ct).ConfigureAwait(false);
            }
        }
    }

    private static string Mount(JsonNode inspect, string destination)
    {
        if (inspect["Mounts"] is not JsonArray mounts)
        {
            return string.Empty;
        }

        foreach (var mount in mounts)
        {
            if (mount is null || DockerEngine.Text(mount["Destination"]) != destination)
            {
                continue;
            }

            return DockerEngine.Text(mount["Type"]) == "volume"
                ? DockerEngine.Text(mount["Name"])
                : DockerEngine.Text(mount["Source"]);
        }

        return string.Empty;
    }

    [GeneratedRegex("/containers/([0-9a-f]{64})/")]
    private static partial Regex ContainerDirectory();
}
