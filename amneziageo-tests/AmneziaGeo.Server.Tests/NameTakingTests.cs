using System.Net;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Names;

namespace AmneziaGeo.Server.Tests;

public class NameTakingTests
{
    [Fact]
    public void WhereAConnectionSentInWasGoingIsReadOutOfWhatTheKernelKeeps()
    {
        var four = new byte[] { 2, 0, 0x01, 0xBB, 203, 0, 113, 7, 0, 0, 0, 0, 0, 0, 0, 0 };
        var six = new byte[28];
        six[0] = 10;
        six[2] = 0x00;
        six[3] = 0x50;
        IPAddress.Parse("2001:db8::7").GetAddressBytes().CopyTo(six, 8);

        Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.7"), 443), NameTarget.Read(four));
        Assert.Equal(new IPEndPoint(IPAddress.Parse("2001:db8::7"), 80), NameTarget.Read(six));
        Assert.Null(NameTarget.Read(four.AsSpan(0, 8)));
        Assert.Null(NameTarget.Read(six.AsSpan(0, 20)));
        Assert.Null(NameTarget.Read(new byte[16]));
    }

    [Fact]
    public void TheConnectionsComeInThroughAPortOfThePanelTheFirewallLetsIn()
    {
        var plan = new FirewallPlan(
            [
                new FirewallPort(FirewallPlan.Udp, 51820, "awg1"),
                new FirewallPort(FirewallPlan.Tcp, 9443, "awg1"),
                new FirewallPort(FirewallPlan.Tcp, 5353, "awg1", "awg1"),
                new FirewallPort(FirewallPlan.Tcp, 8443, FirewallPlan.Panel),
            ],
            ["awg1"]);
        var open = new PanelSettings { Port = 8443 };
        var hidden = new PanelSettings { Port = 8443, Listen = ["127.0.0.1"] };

        var ports = NameDoor.Ports(plan, open);
        var behind = NameDoor.Ports(plan with { Ports = [.. plan.Ports.Take(3)] }, hidden);

        Assert.Equal([8443, 9443], ports);
        Assert.Equal([9443], behind);
        Assert.Equal([9443, 8443], NameDoor.Pick(ports, [PortState.Unknown, PortState.Open]));
        Assert.Equal([8443], NameDoor.Pick(ports, [PortState.Open, PortState.Closed]));
        Assert.Equal([8443, 9443], NameDoor.Pick(ports, []));
        Assert.Empty(NameDoor.Pick(ports, [PortState.Closed, PortState.Closed]));
    }

    [Fact]
    public void PacketsHandedOverWithNoConnectionAcrossTwoLooksTellThatTheHostDropsThem()
    {
        var watch = new NameWatch();
        watch.Start(5);

        Assert.False(watch.Look(2, 5));
        Assert.False(watch.Look(3, 5));
        Assert.True(watch.Look(3, 5));
        Assert.Equal(3, watch.Unreached);
        Assert.False(watch.Look(9, 6));
        Assert.Equal(0, watch.Unreached);
        Assert.False(watch.Look(12, 6));
        Assert.True(watch.Look(13, 6));
    }

    [Fact]
    public void AWatchStartedOverOrLeftWithoutACounterTellsNothing()
    {
        var watch = new NameWatch();
        watch.Start(0);

        Assert.False(watch.Look(4, 0));
        watch.Start(0);
        Assert.False(watch.Look(1, 0));
        Assert.False(watch.Look(2, 0));
        Assert.False(watch.Look(null, 0));
        Assert.Equal(0, watch.Unreached);
        Assert.False(watch.Look(2, 0));
        Assert.False(watch.Look(3, 0));
        Assert.True(watch.Look(3, 0));
    }

    [Fact]
    public void AFullPanelHasRoomAgainOnceAQuarterOfItsConnectionsEnded()
    {
        Assert.False(NameRoom.Full(false, 4095, 4096));
        Assert.True(NameRoom.Full(false, 4096, 4096));
        Assert.True(NameRoom.Full(true, 4000, 4096));
        Assert.True(NameRoom.Full(true, 3073, 4096));
        Assert.False(NameRoom.Full(true, 3072, 4096));
        Assert.True(NameRoom.Full(false, 8, 8));
        Assert.False(NameRoom.Full(true, 6, 8));
    }

    [Fact]
    public void TheGateTellsTheRulesThePortTheConnectionsComeInThrough()
    {
        var gate = new NameGate();

        gate.OpenPort(7319, true, 8443);
        var through = gate.Offer;
        gate.OpenPort(7319, false);
        var plain = gate.Offer;
        gate.ClosePort();

        Assert.Equal((7319, true, 8443), (through?.Port, through?.Six, through?.Door));
        Assert.Equal((7319, false, (int?)null), (plain?.Port, plain?.Six, plain?.Door));
        Assert.Null(gate.Offer);
    }
}
