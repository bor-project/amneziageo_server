using System.Globalization;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// One port of the host carried to a port of a client.
/// </summary>
/// <param name="Protocol">Whether the port is tcp or udp.</param>
/// <param name="From">The port the host listens on.</param>
/// <param name="To">The port of the client the host carries it to.</param>
public sealed record PortForward(string Protocol, int From, int To)
{
    /// <summary>
    /// The name of the first protocol a forward takes.
    /// </summary>
    public const string Tcp = "tcp";

    /// <summary>
    /// The name of the second protocol a forward takes.
    /// </summary>
    public const string Udp = "udp";

    /// <summary>
    /// Reads a forward written as protocol, port of the host and port of the client, telling whether it held.
    /// </summary>
    public static bool TryParse(string? text, out PortForward found)
    {
        found = default!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        var protocol = parts[0].ToLowerInvariant();
        if (protocol != Tcp && protocol != Udp
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var to)
            || !IsPort(from) || !IsPort(to))
        {
            return false;
        }

        found = new PortForward(protocol, from, to);

        return true;
    }

    /// <summary>
    /// Tells whether a number names a port.
    /// </summary>
    public static bool IsPort(int port) => port is > 0 and <= 65535;

    /// <summary>
    /// Writes the forward as protocol, port of the host and port of the client.
    /// </summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Protocol}:{From}:{To}");
}
