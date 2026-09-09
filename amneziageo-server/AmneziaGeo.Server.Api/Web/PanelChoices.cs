using System.Net.NetworkInformation;
using System.Net.Sockets;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// What the host offers the settings of the panel to pick from.
/// </summary>
public static class PanelChoices
{
    /// <summary>
    /// Returns the domains the host holds a certificate for.
    /// </summary>
    public static IReadOnlyList<string> Domains(string root)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        if (Directory.Exists(root))
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (File.Exists(Path.Combine(directory, PanelDefaults.Chain))
                    && File.Exists(Path.Combine(directory, PanelDefaults.Key)))
                {
                    found.Add(Path.GetFileName(directory));
                }
            }
        }

        return [.. found];
    }

    /// <summary>
    /// Returns the addresses the host carries.
    /// </summary>
    public static IReadOnlyList<string> Addresses()
    {
        var found = new List<string>();
        foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (item.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (var address in item.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
                {
                    continue;
                }

                if (address.Address.IsIPv6LinkLocal)
                {
                    continue;
                }

                var text = address.Address.ToString();
                if (!found.Contains(text, StringComparer.Ordinal))
                {
                    found.Add(text);
                }
            }
        }

        return found;
    }
}
