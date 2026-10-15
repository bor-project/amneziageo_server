using System.Buffers.Binary;
using System.Diagnostics;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public class HostChangesTests
{
    [Fact]
    public void AHostThePanelDoesNotHearCountsAsChangedEveryRound()
    {
        using var changes = new HostChanges();

        Assert.True(changes.Take());
        Assert.True(changes.Take());
        Assert.False(changes.Hears);
    }

    [Fact]
    public void AHeardHostCountsAsChangedOnceForTheFirstLookAndThenForEachNews()
    {
        using var changes = new HostChanges();
        changes.Listening();

        var first = changes.Take();
        var still = changes.Take();
        changes.Heard();
        changes.Heard();
        var told = changes.Take();
        var after = changes.Take();

        Assert.True(first);
        Assert.False(still);
        Assert.True(told);
        Assert.False(after);
    }

    [Fact]
    public void AHostThatIsNoLongerHeardCountsAsChangedAgain()
    {
        using var changes = new HostChanges();
        changes.Listening();
        changes.Take();

        changes.Deaf();

        Assert.True(changes.Take());
        Assert.True(changes.Take());
    }

    [Fact]
    public async Task NewsEndsTheWaitBeforeTheRound()
    {
        using var changes = new HostChanges();
        changes.Listening();
        var watch = Stopwatch.StartNew();

        var waiting = changes.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        changes.Heard();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WithoutNewsTheWaitLastsTheRound()
    {
        using var changes = new HostChanges();
        changes.Listening();
        var watch = Stopwatch.StartNew();

        await changes.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250));
    }

    [Theory]
    [InlineData((ushort)16, true)]
    [InlineData((ushort)17, true)]
    [InlineData((ushort)32, true)]
    [InlineData((ushort)33, true)]
    [InlineData((ushort)2, false)]
    [InlineData((ushort)20, false)]
    public void TheKernelToldOfAChangeOfAnInterfaceOrARuleAlone(ushort type, bool told)
    {
        var batch = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(batch, 24);
        BinaryPrimitives.WriteUInt16LittleEndian(batch.AsSpan(4), type);

        Assert.Equal(told, RouteEvents.Told(batch));
    }

    [Fact]
    public void AChangeAfterAnAnswerInOneBatchIsTold()
    {
        var batch = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(batch, 20);
        BinaryPrimitives.WriteUInt16LittleEndian(batch.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(batch.AsSpan(20), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(batch.AsSpan(24), 17);

        Assert.True(RouteEvents.Told(batch));
        Assert.False(RouteEvents.Told(batch.AsSpan(0, 20)));
    }
}
