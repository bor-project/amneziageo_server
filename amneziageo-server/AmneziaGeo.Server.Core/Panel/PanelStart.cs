using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// The settings a panel that holds none starts under.
/// </summary>
public static class PanelStart
{
    /// <summary>
    /// The head of the environment variables that list what the panel listens on, one entry each.
    /// </summary>
    public const string ListenVariable = "Web__Listen__";

    /// <summary>
    /// The environment variable that names the path of the panel.
    /// </summary>
    public const string PathVariable = "Web__Path";

    /// <summary>
    /// What the panel listens on when the environment names nothing, as the package and the image start it.
    /// </summary>
    public static readonly string[] Listen = [$"{PanelEdit.Loopback}:{PanelDefaults.Port}"];

    /// <summary>
    /// Returns the settings the Web variables of an environment start a panel under.
    /// </summary>
    public static PanelSettings Of(IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        var listen = new SortedDictionary<int, string>();
        var path = string.Empty;
        foreach (var (name, value) in variables)
        {
            if (name.StartsWith(ListenVariable, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.AsSpan(ListenVariable.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                listen[index] = value;

                continue;
            }

            if (string.Equals(name, PathVariable, StringComparison.OrdinalIgnoreCase))
            {
                path = value;
            }
        }

        return Of(listen.Count > 0 ? [.. listen.Values] : Listen, path);
    }

    /// <summary>
    /// Returns the settings a listen list and a path start a panel under, a fresh path for an empty one.
    /// </summary>
    public static PanelSettings Of(IReadOnlyList<string> listen, string path)
    {
        ArgumentNullException.ThrowIfNull(listen);
        ArgumentNullException.ThrowIfNull(path);

        var port = Split(listen[0]).Port;
        var chosen = path.Length > 0 ? PanelEdit.PathOf(path) : PanelDefaults.FreshPath();
        var addresses = new List<string>();
        foreach (var entry in listen)
        {
            var (host, own) = Split(entry);
            if (own != port)
            {
                continue;
            }

            if (host is "*")
            {
                return new PanelSettings { Port = port, Path = chosen };
            }

            if (IPAddress.TryParse(host, out var address))
            {
                addresses.Add(address.ToString());

                continue;
            }

            addresses.AddRange(Addresses(host).Select(item => item.ToString()));
        }

        return new PanelSettings { Listen = PanelList.Of(addresses), Port = port, Path = chosen };
    }

    /// <summary>
    /// Splits an entry of a listen list into the host and the port.
    /// </summary>
    public static (string Host, int Port) Split(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var text = entry.Trim();
        var mark = text.LastIndexOf(':');
        if (mark <= 0 || !int.TryParse(text.AsSpan(mark + 1), CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"the panel is told to listen on {entry}, which is not an address or an interface with a port");
        }

        return (text[..mark].Trim('[', ']'), port);
    }

    /// <summary>
    /// Returns the addresses an interface of the host carries, link-local ones left out.
    /// </summary>
    public static IEnumerable<IPAddress> Addresses(string name)
    {
        var found = Array.Find(
            NetworkInterface.GetAllNetworkInterfaces(),
            item => string.Equals(item.Name, name, StringComparison.Ordinal));

        if (found is null)
        {
            return [];
        }

        return found.GetIPProperties().UnicastAddresses
            .Select(item => item.Address)
            .Where(item => item.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Where(item => !item.IsIPv6LinkLocal);
    }
}
