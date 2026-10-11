using System.Net;
using AmneziaGeo.Server.Api.Services;

namespace AmneziaGeo.Server.Tests;

/// <summary>
/// A client that asks for a websocket again at once after every refusal is slowed down by the server: the first
/// refusals of an address in a row go out at once, each later answer is held, and the row is counted anew once the
/// address has been silent for a while.
/// </summary>
public sealed class RefusalHoldTests
{
    private static readonly IPAddress Caller = IPAddress.Parse("203.0.113.9");

    private readonly Clock _clock = new(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void TheFirstRefusalsOfAnAddress_GoOutAtOnce()
    {
        var hold = new RefusalHold(_clock);

        var notes = Enumerable.Range(0, RefusalHold.Free).Select(_ => hold.Note(Caller)).ToArray();

        Assert.All(notes, one => Assert.Equal(TimeSpan.Zero, one.Wait));
        Assert.Equal([true, false, false], notes.Select(one => one.First).ToArray());
        Assert.Equal([1, 2, 3], notes.Select(one => one.Count).ToArray());
        Assert.DoesNotContain(notes, one => one.Begins);
        Assert.Equal(0, hold.Waiting(Caller));
    }

    [Fact]
    public void AnAddressThatKeepsAsking_HasItsAnswersHeld_AndIsToldOfOnce()
    {
        var hold = new RefusalHold(_clock);
        for (var index = 0; index < RefusalHold.Free; index++)
        {
            hold.Note(Caller);
            _clock.Pass(TimeSpan.FromSeconds(1));
        }

        var fourth = hold.Note(Caller);
        hold.Left(Caller);
        var fifth = hold.Note(Caller);

        Assert.Equal(RefusalHold.Wait, fourth.Wait);
        Assert.True(fourth.Begins);
        Assert.False(fourth.First);
        Assert.Equal(4, fourth.Count);
        Assert.Equal(TimeSpan.FromSeconds(3), fourth.Lasted);
        Assert.Equal(RefusalHold.Wait, fifth.Wait);
        Assert.False(fifth.Begins);
        Assert.Equal(1, hold.Waiting(Caller));
    }

    [Fact]
    public void AnAddressThatFellSilent_IsCountedAnew()
    {
        var hold = new RefusalHold(_clock);
        for (var index = 0; index < RefusalHold.Free + 2; index++)
        {
            hold.Note(Caller);
            hold.Left(Caller);
        }

        _clock.Pass(RefusalHold.Quiet);
        var still = hold.Note(Caller);
        hold.Left(Caller);
        _clock.Pass(RefusalHold.Quiet + TimeSpan.FromSeconds(1));
        var anew = hold.Note(Caller);

        Assert.Equal(RefusalHold.Wait, still.Wait);
        Assert.Equal(TimeSpan.Zero, anew.Wait);
        Assert.True(anew.First);
        Assert.Equal(1, anew.Count);
    }

    [Fact]
    public void EveryAddressHasARowOfItsOwn_AndAMappedOneIsTheSameAddress()
    {
        var hold = new RefusalHold(_clock);
        for (var index = 0; index < RefusalHold.Free; index++)
        {
            hold.Note(Caller);
        }

        var other = hold.Note(IPAddress.Parse("203.0.113.10"));
        var mapped = hold.Note(Caller.MapToIPv6());
        var unknown = hold.Note(null);

        Assert.True(other.First);
        Assert.Equal(TimeSpan.Zero, other.Wait);
        Assert.Equal(RefusalHold.Wait, mapped.Wait);
        Assert.Equal(4, mapped.Count);
        Assert.True(unknown.First);
    }

    [Fact]
    public void AWebSocketWhoseFrontIsDown_IsHeldFromTheFirst_AndToldOfOnlyOnceTheAddressKeepsAsking()
    {
        var hold = new RefusalHold(_clock);

        var first = hold.Note(Caller, down: true);
        hold.Left(Caller);
        var second = hold.Note(Caller, down: true);
        hold.Left(Caller);
        var third = hold.Note(Caller, down: true);
        hold.Left(Caller);
        var fourth = hold.Note(Caller, down: true);

        Assert.Equal(RefusalHold.Wait, first.Wait);
        Assert.True(first.First);
        Assert.Equal([false, false, false, true], new[] { first, second, third, fourth }.Select(one => one.Begins).ToArray());
        Assert.Equal(1, hold.Waiting(Caller));
    }

    [Fact]
    public void NoMoreAnswersAreHeldForAnAddressAtATime_ThanItIsGiven()
    {
        var hold = new RefusalHold(_clock);
        for (var index = 0; index < RefusalHold.Free; index++)
        {
            hold.Note(Caller);
        }

        var held = Enumerable.Range(0, RefusalHold.Parked).Select(_ => hold.Note(Caller)).ToArray();
        var past = hold.Note(Caller);
        hold.Left(Caller);
        var again = hold.Note(Caller);

        Assert.All(held, one => Assert.Equal(RefusalHold.Wait, one.Wait));
        Assert.Equal(TimeSpan.Zero, past.Wait);
        Assert.Equal(RefusalHold.Wait, again.Wait);
        Assert.Equal(RefusalHold.Parked, hold.Waiting(Caller));
    }
}
