using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Tests;

public class OutboundStoreTests
{
    [Fact]
    public async Task AFreshInstallCarriesTheWayOutThroughTheHost()
    {
        using var bench = new Bench();

        var held = Assert.Single(await bench.Outbounds.ListAsync(CancellationToken.None));

        Assert.Equal(OutboundDefaults.DirectName, held.Name);
        Assert.Equal(OutboundKind.Local, held.Kind);
        Assert.Equal(OutboundRules.MainTable, held.Table);
        Assert.True(held.IsEnabled);
    }

    [Fact]
    public async Task ATunnelTakesTheFirstFreeMarkAndThePlaceAtTheEnd()
    {
        using var bench = new Bench();

        var added = await bench.Outbounds.AddAsync(Tunnel("awgbor"), CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        Assert.Equal(OutboundRules.FirstMark + 1, added.Record!.Mark);
        Assert.Equal(OutboundRules.FirstTable + 1, added.Record.Table);
        Assert.Equal(2, added.Record.Position);
        Assert.Equal(added.Record.PublicKey, AmneziaGeo.Server.Core.Crypto.Curve25519.PublicOf(added.Record.PrivateKey));
    }

    [Fact]
    public async Task AnOutboundUnderATakenNameIsRefused()
    {
        using var bench = new Bench();
        await bench.Outbounds.AddAsync(Tunnel("awgbor"), CancellationToken.None);

        var again = await bench.Outbounds.AddAsync(Tunnel("awgbor"), CancellationToken.None);

        Assert.Equal(OutboundOutcome.NameTaken, again.Outcome);
    }

    [Fact]
    public async Task ASettingOutOfShapeIsRefusedWithItsCode()
    {
        using var bench = new Bench();

        var added = await bench.Outbounds.AddAsync(Tunnel("awgbor") with { Host = string.Empty }, CancellationToken.None);

        Assert.Equal(OutboundOutcome.Invalid, added.Outcome);
        Assert.Equal("bad-host", added.Code);
    }

    [Fact]
    public async Task ChangingAnOutboundKeepsItsMarkAndItsPlace()
    {
        using var bench = new Bench();
        var added = await bench.Outbounds.AddAsync(Tunnel("awgbor"), CancellationToken.None);

        var changed = await bench.Outbounds.ChangeAsync(
            added.Record!.Id,
            Tunnel("awgfar") with { Port = 51830 },
            CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal("awgfar", changed.Record!.Name);
        Assert.Equal(51830, changed.Record.Port);
        Assert.Equal(added.Record.Mark, changed.Record.Mark);
        Assert.Equal(added.Record.Position, changed.Record.Position);
    }

    [Fact]
    public async Task AnOutboundIsTurnedOffAndBackOn()
    {
        using var bench = new Bench();
        var added = await bench.Outbounds.AddAsync(Tunnel("awgbor"), CancellationToken.None);

        var off = await bench.Outbounds.SwitchAsync(added.Record!.Id, false, CancellationToken.None);
        var on = await bench.Outbounds.SwitchAsync(added.Record.Id, true, CancellationToken.None);

        Assert.False(off.Record!.IsEnabled);
        Assert.True(on.Record!.IsEnabled);
    }

    [Fact]
    public async Task AnOutboundSwapsPlacesWithItsNeighbourAndStopsAtTheEdge()
    {
        using var bench = new Bench();
        var added = await bench.Outbounds.AddAsync(Tunnel("awgbor"), CancellationToken.None);

        await bench.Outbounds.MoveAsync(added.Record!.Id, true, CancellationToken.None);
        var order = await bench.Outbounds.ListAsync(CancellationToken.None);
        await bench.Outbounds.MoveAsync(added.Record.Id, true, CancellationToken.None);
        var again = await bench.Outbounds.ListAsync(CancellationToken.None);

        Assert.Equal(["awgbor", OutboundDefaults.DirectName], order.Select(one => one.Name));
        Assert.Equal(["awgbor", OutboundDefaults.DirectName], again.Select(one => one.Name));
    }

    [Fact]
    public async Task ARemovedOutboundIsGoneAndItsMarkComesBack()
    {
        using var bench = new Bench();
        var added = await bench.Outbounds.AddAsync(Tunnel("awgbor"), CancellationToken.None);

        await bench.Outbounds.RemoveAsync(added.Record!.Id, CancellationToken.None);
        var next = await bench.Outbounds.AddAsync(Tunnel("awgfar"), CancellationToken.None);

        Assert.Null(await bench.Outbounds.FindAsync(added.Record.Id, CancellationToken.None));
        Assert.Equal(added.Record.Mark, next.Record!.Mark);
    }

    [Fact]
    public async Task ACommandAgainstAnOutboundThePanelDoesNotHoldIsRefused()
    {
        using var bench = new Bench();

        Assert.Equal(OutboundOutcome.Unknown, (await bench.Outbounds.RemoveAsync(404, CancellationToken.None)).Outcome);
        Assert.Equal(OutboundOutcome.Unknown, (await bench.Outbounds.MoveAsync(404, true, CancellationToken.None)).Outcome);
        Assert.Equal(
            OutboundOutcome.Unknown,
            (await bench.Outbounds.SwitchAsync(404, false, CancellationToken.None)).Outcome);
    }

    private static OutboundConfig Tunnel(string name) => new()
    {
        Name = name,
        Kind = OutboundKind.Wg,
        IsEnabled = true,
        Host = "bor.sytes.net",
        Port = 51821,
        PrivateKey = "OMMbTfBGZmzVOenkLmL2hFRuMbAG9pOZzTZ4L1H+wF0=",
        PeerKey = "eOWG0GXBLrpZOZ+FCwCJTvZ5rBEKHZ8NPvxNM3nJPl4=",
        Address = ["10.8.1.5/32"],
        Mtu = 1380,
        Keepalive = 25,
    };
}
