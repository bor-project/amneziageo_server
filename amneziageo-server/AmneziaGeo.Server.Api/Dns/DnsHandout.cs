using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// The addresses of the resolver a client is handed in its configuration.
/// </summary>
public static class DnsHandout
{
    /// <summary>
    /// Returns the addresses of the resolver inside the tunnel of a configuration, none when it answers elsewhere.
    /// </summary>
    public static IReadOnlyList<string> For(ServerConfig config, DnsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled || settings.Port != DnsDefaults.Port)
        {
            return [];
        }

        var own = config.Address.Select(Bare).Where(one => one.Length > 0).ToArray();

        return settings.Listen.Count == 0
            ? own
            : [.. own.Where(one => settings.Listen.Contains(one, StringComparer.Ordinal))];
    }

    private static string Bare(string range)
    {
        var slash = range.IndexOf('/', StringComparison.Ordinal);

        return (slash < 0 ? range : range[..slash]).Trim();
    }
}
