using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Awg.Uapi;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// Resolves a generic netlink family of the kernel.
/// </summary>
public static class FamilyCommand
{
    /// <summary>
    /// Prints the identifier of a family and returns the exit code.
    /// </summary>
    public static int Run(Arguments args)
    {
        var name = args.At(1) ?? WgUapi.FamilyName;
        using var socket = new NetlinkSocket();
        var id = GenericNetlink.Resolve(socket, name);

        Terminal.Say(id is null
            ? $"the '{name}' family is not registered"
            : $"the '{name}' family is {id}");

        return id is null ? 1 : 0;
    }
}
