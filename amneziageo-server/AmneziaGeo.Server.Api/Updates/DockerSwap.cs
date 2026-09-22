using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// Makes the container of the panel over onto the image of a release.
/// </summary>
public static class DockerSwap
{
    private static readonly string[] Carried =
        ["Cmd", "Entrypoint", "WorkingDir", "User", "StopSignal", "StopTimeout", "Healthcheck", "ExposedPorts"];

    private static readonly string[] Ends = ["IPAMConfig", "Links", "DriverOpts"];

    /// <summary>
    /// Returns the container to make out of the one that runs, on another image.
    /// </summary>
    /// <param name="container">What the daemon tells of the container that runs.</param>
    /// <param name="image">What the daemon tells of the image that container runs.</param>
    /// <param name="picture">The image the new container runs.</param>
    public static JsonObject Spec(JsonNode container, JsonNode image, string picture)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(image);

        var config = container["Config"];
        var baseline = image["Config"];
        var host = container["HostConfig"]?.DeepClone()?.AsObject() ?? [];
        Hold(host, container["Mounts"]);
        var spec = new JsonObject
        {
            ["Image"] = picture,
            ["Env"] = Lines(config?["Env"], baseline?["Env"]),
            ["Labels"] = Marks(config?["Labels"] as JsonObject, baseline?["Labels"] as JsonObject),
            ["Tty"] = Flag(config?["Tty"]),
            ["OpenStdin"] = Flag(config?["OpenStdin"]),
            ["HostConfig"] = host,
        };

        foreach (var field in Carried)
        {
            if (Apart(config?[field], baseline?[field]) is { } kept)
            {
                spec[field] = kept;
            }
        }

        var shared = Shared(DockerEngine.Text(host["NetworkMode"]));
        if (!shared && DockerEngine.Text(config?["Hostname"]) is { Length: > 0 } hostname)
        {
            spec["Hostname"] = hostname;
        }

        if (!shared && Joined(container) is { } networks)
        {
            spec["NetworkingConfig"] = networks;
        }

        return spec;
    }

    /// <summary>
    /// Tells whether a container shares the network of the host or of another container.
    /// </summary>
    public static bool Shared(string mode)
    {
        ArgumentNullException.ThrowIfNull(mode);

        return mode is "host" or "none" || mode.StartsWith("container:", StringComparison.Ordinal);
    }

    private static JsonArray Lines(JsonNode? mine, JsonNode? theirs)
    {
        var held = Words(theirs);
        var kept = new JsonArray();
        foreach (var line in Words(mine))
        {
            if (!held.Contains(line))
            {
                kept.Add(line);
            }
        }

        return kept;
    }

    private static JsonObject Marks(JsonObject? mine, JsonObject? theirs)
    {
        var kept = new JsonObject();
        if (mine is null)
        {
            return kept;
        }

        foreach (var (name, value) in mine)
        {
            if (!Same(value, theirs?[name]))
            {
                kept[name] = value?.DeepClone();
            }
        }

        return kept;
    }

    private static void Hold(JsonObject host, JsonNode? mounts)
    {
        if (mounts is not JsonArray held)
        {
            return;
        }

        var known = Targets(host);
        var added = new List<JsonObject>();
        foreach (var mount in held)
        {
            var target = DockerEngine.Text(mount?["Destination"]);
            var name = DockerEngine.Text(mount?["Name"]);
            if (DockerEngine.Text(mount?["Type"]) != "volume" || target.Length == 0 || name.Length == 0 || known.Contains(target))
            {
                continue;
            }

            added.Add(new JsonObject
            {
                ["Type"] = "volume",
                ["Source"] = name,
                ["Target"] = target,
                ["ReadOnly"] = mount?["RW"] is JsonValue rw && !rw.GetValue<bool>(),
            });
        }

        if (added.Count == 0)
        {
            return;
        }

        if (host["Mounts"] is not JsonArray list)
        {
            list = [];
            host["Mounts"] = list;
        }

        foreach (var mount in added)
        {
            list.Add(mount);
        }
    }

    private static HashSet<string> Targets(JsonObject host)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bind in Words(host["Binds"]))
        {
            var parts = bind.Split(':');
            if (parts.Length >= 2)
            {
                known.Add(parts[1]);
            }
        }

        if (host["Mounts"] is JsonArray mounts)
        {
            foreach (var mount in mounts)
            {
                known.Add(DockerEngine.Text(mount?["Target"]));
            }
        }

        return known;
    }

    private static JsonObject? Joined(JsonNode container)
    {
        if (container["NetworkSettings"]?["Networks"] is not JsonObject networks)
        {
            return null;
        }

        var id = DockerEngine.Text(container["Id"]);
        var joined = new JsonObject();
        foreach (var (name, settings) in networks)
        {
            var one = new JsonObject();
            if (Aliases(settings?["Aliases"], id) is { Count: > 0 } aliases)
            {
                one["Aliases"] = aliases;
            }

            foreach (var field in Ends)
            {
                if (settings?[field] is { } value && value.GetValueKind() != JsonValueKind.Null)
                {
                    one[field] = value.DeepClone();
                }
            }

            joined[name] = one;
        }

        return joined.Count == 0 ? null : new JsonObject { ["EndpointsConfig"] = joined };
    }

    private static JsonArray Aliases(JsonNode? held, string id)
    {
        var kept = new JsonArray();
        foreach (var alias in Words(held))
        {
            if (!id.StartsWith(alias, StringComparison.Ordinal))
            {
                kept.Add(alias);
            }
        }

        return kept;
    }

    private static JsonNode? Apart(JsonNode? mine, JsonNode? theirs) =>
        mine is null || mine.GetValueKind() == JsonValueKind.Null || Same(mine, theirs) ? null : mine.DeepClone();

    private static bool Same(JsonNode? one, JsonNode? other) =>
        (one?.ToJsonString() ?? "null") == (other?.ToJsonString() ?? "null");

    private static bool Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var held) && held;

    private static List<string> Words(JsonNode? list) =>
        list is JsonArray array ? [.. array.Select(DockerEngine.Text)] : [];
}
