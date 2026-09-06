using AmneziaGeo.Server.Awg.Netlink;
using Xunit;

namespace AmneziaGeo.Server.Tests;

public class GenericNetlinkTests
{
    [Fact]
    public void TheControlFamilyResolvesToItsWellKnownIdentifier()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        using var socket = new NetlinkSocket();

        Assert.Equal((ushort)16, GenericNetlink.Resolve(socket, "nlctrl"));
    }

    [Fact]
    public void AFamilyTheKernelDoesNotCarryResolvesToNothing()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        using var socket = new NetlinkSocket();

        Assert.Null(GenericNetlink.Resolve(socket, "nosuchfamily"));
    }
}
