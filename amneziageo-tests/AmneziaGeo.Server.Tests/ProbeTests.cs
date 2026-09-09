using System.Net;
using System.Text;
using AmneziaGeo.Server.Api.Outbounds;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Probe;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public class ProbeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheQuestionCarriesTheNameItAsksFor()
    {
        var packet = ProbeQuestion.Packet("example.com", 0x1234);

        Assert.Equal(29, packet.Length);
        Assert.Equal(0x12, packet[0]);
        Assert.Equal(0x34, packet[1]);
        Assert.Equal(7, packet[12]);
        Assert.Equal("example", Encoding.ASCII.GetString(packet, 13, 7));
        Assert.Equal(3, packet[20]);
        Assert.Equal("com", Encoding.ASCII.GetString(packet, 21, 3));
        Assert.Equal(0, packet[24]);
        Assert.Equal(1, packet[26]);
        Assert.Equal(1, packet[28]);
    }

    [Fact]
    public void AnAnswerUnderTheNumberOfTheQuestionIsTakenAsOne()
    {
        Assert.True(ProbeQuestion.Answers(Answer(0x1234), 0x1234));
    }

    [Fact]
    public void AnAnswerUnderAnotherNumberIsNotTakenAsOne()
    {
        Assert.False(ProbeQuestion.Answers(Answer(0x1234), 0x4321));
    }

    [Fact]
    public void AQuestionComingBackIsNotTakenForAnAnswer()
    {
        Assert.False(ProbeQuestion.Answers(ProbeQuestion.Packet("example.com", 0x1234), 0x1234));
    }

    [Fact]
    public void OneProbeThatMissesLeavesTheOutboundCarryingTraffic()
    {
        var live = new ProbeLive();
        live.Keep("awgbor", ProbeOutcome.Reached, Noon);

        Assert.False(live.Keep("awgbor", ProbeOutcome.Missed, Noon));
        Assert.True(live.Find("awgbor")?.IsReached);
    }

    [Fact]
    public void TwoProbesThatMissInARowTakeTheOutboundOff()
    {
        var live = new ProbeLive();
        live.Keep("awgbor", ProbeOutcome.Reached, Noon);
        live.Keep("awgbor", ProbeOutcome.Missed, Noon);

        Assert.True(live.Keep("awgbor", ProbeOutcome.Missed, Noon));
        Assert.False(live.Find("awgbor")?.IsReached);
        Assert.Equal(2, live.Find("awgbor")?.Falls);
    }

    [Fact]
    public void AProbeThatAnswersBringsTheOutboundBack()
    {
        var live = new ProbeLive();
        live.Keep("awgbor", ProbeOutcome.Missed, Noon);
        live.Keep("awgbor", ProbeOutcome.Missed, Noon);

        Assert.True(live.Keep("awgbor", ProbeOutcome.Reached, Noon));
        Assert.Equal(0, live.Find("awgbor")?.Falls);
    }

    [Fact]
    public void AProbeThatNeverWentOutLeavesTheVerdictAsItWas()
    {
        var live = new ProbeLive();
        live.Keep("awgbor", ProbeOutcome.Reached, Noon);

        Assert.False(live.Keep("awgbor", ProbeOutcome.Skipped, Noon));
        Assert.True(live.Find("awgbor")?.IsReached);
    }

    [Fact]
    public void TheVerdictsOfTheOutboundsThatAreGoneAreDropped()
    {
        var live = new ProbeLive();
        live.Keep("awgbor", ProbeOutcome.Reached, Noon);
        live.Keep("awgoff", ProbeOutcome.Reached, Noon);

        live.Hold(["awgbor"]);

        Assert.NotNull(live.Find("awgbor"));
        Assert.Null(live.Find("awgoff"));
    }

    [Fact]
    public void AnOutboundTheProbeDoesNotReachCarriesNothing()
    {
        var state = new OutboundState("awgbor", true, "", Noon, 0, 0, true, "", new ProbeReading(false, 2, Noon));

        Assert.True(state.IsAlive);
        Assert.False(state.Carries);
    }

    [Fact]
    public void AnOutboundThatWasNeverProbedCarriesWhatTheHandshakeSays()
    {
        var state = new OutboundState("awgbor", true, "", Noon, 0, 0, true, "");

        Assert.True(state.Carries);
    }

    [Fact]
    public void AProbeToSomethingThatIsNotANameServerIsRefused()
    {
        var fault = OutboundRules.CheckProbe(Tunnel() with { Probe = "not a server" });

        Assert.Equal("bad-probe", fault?.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4000)]
    public void AProbeUnderAPeriodTheServerDoesNotTakeIsRefused(int every)
    {
        var fault = OutboundRules.CheckProbe(Tunnel() with { ProbeEvery = every });

        Assert.Equal("bad-probe", fault?.Code);
    }

    [Fact]
    public void AnOutboundWithoutAProbeHolds()
    {
        Assert.Null(OutboundRules.CheckProbe(Tunnel() with { Probe = "", ProbeEvery = 0 }));
    }

    [Fact]
    public async Task TheRunnerKeepsWhatTheProbeCameTo()
    {
        var live = new ProbeLive();
        var runner = Runner(new Link(ProbeOutcome.Missed), live, new Clock(Noon));

        Assert.False(await runner.RunAsync(Tunnel(), CancellationToken.None));
        Assert.True(await runner.RunAsync(Tunnel(), CancellationToken.None));
        Assert.False(live.Find("awgbor")?.IsReached);
    }

    [Fact]
    public async Task AnOutboundIsDueAProbeOnlyAfterItsPeriodPassed()
    {
        var live = new ProbeLive();
        var clock = new Clock(Noon);
        var runner = Runner(new Link(ProbeOutcome.Reached), live, clock);

        Assert.True(runner.IsDue(Tunnel()));
        await runner.RunAsync(Tunnel(), CancellationToken.None);
        Assert.False(runner.IsDue(Tunnel()));

        clock.Pass(TimeSpan.FromSeconds(31));

        Assert.True(runner.IsDue(Tunnel()));
    }

    [Fact]
    public void AnOutboundThatIsOffIsNeverDueAProbe()
    {
        var runner = Runner(new Link(ProbeOutcome.Reached), new ProbeLive(), new Clock(Noon));

        Assert.False(runner.IsDue(Tunnel() with { IsEnabled = false }));
        Assert.False(runner.IsDue(Tunnel() with { Probe = "" }));
    }

    private static ProbeRunner Runner(IProbeLink link, ProbeLive live, TimeProvider time) =>
        new(link, live, time, NullLogger<ProbeRunner>.Instance);

    private static OutboundConfig Tunnel() => new()
    {
        Name = "awgbor",
        Kind = OutboundKind.Wg,
        Mark = 0xA602,
        Table = 42602,
        Probe = ProbeDefaults.Address,
        ProbeEvery = ProbeDefaults.Every,
    };

    private static byte[] Answer(ushort id)
    {
        var packet = ProbeQuestion.Packet("example.com", id);
        packet[2] |= 0x80;

        return packet;
    }

    private sealed class Link : IProbeLink
    {
        private readonly ProbeOutcome _outcome;

        public Link(ProbeOutcome outcome)
        {
            _outcome = outcome;
        }

        public Task<ProbeOutcome> ReachAsync(
            IPEndPoint server, uint mark, string name, TimeSpan wait, CancellationToken ct) =>
            Task.FromResult(_outcome);
    }
}
