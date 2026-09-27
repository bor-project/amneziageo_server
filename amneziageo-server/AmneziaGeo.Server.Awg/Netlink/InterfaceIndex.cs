using System.Runtime.InteropServices;
using System.Text;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// Names the interfaces of the network namespace the panel runs in by their index.
/// </summary>
public static partial class InterfaceIndex
{
    private const int NameLength = 16;

    /// <summary>
    /// Returns the name of the interface with an index, or empty when there is none.
    /// </summary>
    public static string Name(uint index)
    {
        if (index == 0 || !OperatingSystem.IsLinux())
        {
            return string.Empty;
        }

        var name = new byte[NameLength];
        if (IndexToName(index, name) == 0)
        {
            return string.Empty;
        }

        var end = Array.IndexOf(name, (byte)0);

        return Encoding.UTF8.GetString(name, 0, end < 0 ? name.Length : end);
    }

    [LibraryImport("libc", EntryPoint = "if_indextoname")]
    private static partial nint IndexToName(uint index, byte[] name);
}
