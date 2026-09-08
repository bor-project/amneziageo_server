using System.Text;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Where the interface files of the endpoints live.
/// </summary>
public sealed class InterfaceFileOptions
{
    /// <summary>
    /// The section the settings are read from.
    /// </summary>
    public const string Section = "Endpoints";

    /// <summary>
    /// The directory the interface files live in.
    /// </summary>
    public string Directory { get; set; } = "/etc/amnezia/amneziawg";

    /// <summary>
    /// Whether the clients are written into the interface file the host boots from.
    /// </summary>
    public bool KeepFile { get; set; } = true;
}

/// <summary>
/// Writes the clients of an endpoint into the interface file the host boots from.
/// </summary>
public sealed class InterfaceFile
{
    private readonly InterfaceFileOptions _options;

    /// <summary>
    /// ctor
    /// </summary>
    public InterfaceFile(InterfaceFileOptions options) => _options = options;

    /// <summary>
    /// Returns the path of the file an endpoint is written to.
    /// </summary>
    public string PathOf(string name) => Path.Combine(_options.Directory, name + ".conf");

    /// <summary>
    /// Tells whether the host carries a file for an endpoint.
    /// </summary>
    public bool Has(string name) => _options.KeepFile && File.Exists(PathOf(name));

    /// <summary>
    /// Writes the clients of an endpoint into its file, keeping the settings above them.
    /// </summary>
    public async Task WriteAsync(ServerConfig config, IReadOnlyList<TunnelClient> clients, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(clients);

        var path = PathOf(config.Name);
        if (!Has(config.Name))
        {
            return;
        }

        var held = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(path, Text(Head(held), config, clients), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the settings of an interface file, up to the clients written under them.
    /// </summary>
    public static string Head(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Trim().StartsWith("[Peer", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            lines.Add(line.TrimEnd());
        }

        while (lines.Count > 0 && (lines[^1].Length == 0 || lines[^1].TrimStart().StartsWith('#')))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Returns the interface file made of settings and the clients an endpoint takes.
    /// </summary>
    public static string Text(string head, ServerConfig config, IReadOnlyList<TunnelClient> clients)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(clients);

        var text = new StringBuilder(head);
        foreach (var client in clients.Where(one => one.IsEnabled).OrderBy(one => one.Name, StringComparer.Ordinal))
        {
            text.Append("\n\n# ").Append(client.Name).Append('\n');
            text.Append("[Peer]\n");
            text.Append("PublicKey = ").Append(client.PublicKey).Append('\n');
            var preshared = ClientText.Preshared(config, client);
            if (preshared.Length > 0)
            {
                text.Append("PresharedKey = ").Append(preshared).Append('\n');
            }

            text.Append("AllowedIPs = ").Append(string.Join(", ", client.Address)).Append('\n');
        }

        return text.Append('\n').ToString();
    }
}
