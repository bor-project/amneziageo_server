using System.Buffers.Binary;
using System.Net;
using System.Text;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class AccessTests
{
    private const ushort Group = AccessDefaults.Group;

    private static readonly byte[] Ip = GeoBuilder.Ip(("RU", ["77.88.8.0/24", "2a02:6b8::/32"]));

    private static readonly byte[] Site = GeoBuilder.Site(("YOUTUBE", ["youtube.com", "ytimg.com"]));

    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "direct", Kind = OutboundKind.Local, Mark = 0xA601, Table = OutboundRules.MainTable },
        new() { Name = "awgbor", Kind = OutboundKind.Wg, Mark = 0xA602, Table = 42602 },
        new() { Name = "awgoff", Kind = OutboundKind.Wg, Mark = 0xA603, Table = 42603, IsEnabled = false },
    ];

    private static readonly DnsSettings Resolver = DnsDefaults.Settings with { IsEnabled = true, Upstreams = ["1.1.1.1"] };

    [Fact]
    public void EveryRuleLogsWhatItDecidesUnderItsOwnPrefix()
    {
        var plan = Plan(
            Out("awgbor") with { Targets = ["geoip:ru"] },
            RouteDefaults.Fresh("no ads") with { Id = 7, Position = 2, Action = RouteAction.Block, Targets = ["geosite:youtube"] },
            RouteDefaults.Fresh("home") with { Id = 8, Position = 3, Action = RouteAction.Direct, Targets = ["1.2.3.0/24"] })
            with { Journal = Group };

        var text = RouteRuleset.Text(plan);

        Assert.Contains("ip daddr @r1v4 meta mark set 0xa602 log prefix \"ag:o:1\" group 7317 return", text, StringComparison.Ordinal);
        Assert.Contains("ip daddr @n7v4 log prefix \"ag:b:7\" group 7317 drop", text, StringComparison.Ordinal);
        Assert.Contains("ip daddr @r8v4 log prefix \"ag:d:8\" group 7317 return", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AConnectionNoRuleTookIsLoggedLastInTheDecision()
    {
        var text = RouteRuleset.Text(Plan(Out("awgbor") with { Targets = ["geoip:ru"] }) with { Journal = Group });

        Assert.Contains("\t\tlog prefix \"ag:n\" group 7317\n\t}\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WhileTheLogRunsItsConnectionsAreWatchedForWhatComesBack()
    {
        var text = RouteRuleset.Text(Plan(Out("awgbor") with { Targets = ["geoip:ru"] }) with { Journal = Group });

        Assert.Contains("\t\tjump decide\n\t\tct mark set meta mark or 0x00010000\n", text, StringComparison.Ordinal);
        Assert.Contains(
            "\tchain answer {\n\t\ttype filter hook postrouting priority mangle; policy accept;\n" +
            "\t\toifname != { \"awg1\", \"awg2\" } accept\n\t\tct direction original accept\n" +
            "\t\tct mark and 0x00050000 != 0x00010000 accept\n",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "tcp flags & (fin | rst | psh) != 0 ct mark set ct mark or 0x00060000 log prefix \"ag:r\" group 7317 accept",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "meta l4proto tcp ct mark set ct mark or 0x00020000 log prefix \"ag:r\" group 7317 accept",
            text,
            StringComparison.Ordinal);
        Assert.Contains("\t\tct mark set ct mark or 0x00060000 log prefix \"ag:r\" group 7317\n\t}\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTheLogNothingIsWatched()
    {
        var text = RouteRuleset.Text(Plan(Out("awgbor") with { Targets = ["geoip:ru"] }));

        Assert.Contains("\t\tjump decide\n\t\tmeta mark != 0x00000000 ct mark set meta mark\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("chain answer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0x00010000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkOfTheWayOutIsTakenBackWithoutTheBitsOfTheLog()
    {
        const string line = "ct mark and 0x0000ffff != 0x00000000 meta mark set ct mark and 0x0000ffff accept";
        const uint bits = AccessDefaults.Watching | AccessDefaults.Answered | AccessDefaults.Settled;

        Assert.Contains(line, RouteRuleset.Text(Plan(Out("awgbor"))), StringComparison.Ordinal);
        Assert.Contains(line, RouteRuleset.Text(Plan(Out("awgbor")) with { Journal = Group }), StringComparison.Ordinal);
        Assert.Equal(0u, bits & OutboundRules.LastMark);
    }

    [Fact]
    public void WithoutTheLogTheRulesetLogsNothing()
    {
        var plan = Plan(Out("awgbor") with { Targets = ["geoip:ru"] });

        Assert.DoesNotContain("log prefix", RouteRuleset.Text(plan), StringComparison.Ordinal);
        Assert.Equal(RouteRuleset.Lines(Assert.Single(plan.Legs)), RouteRuleset.Lines(Assert.Single(plan.Legs), null));
    }

    [Fact]
    public void AHeldRuleLogsWhatItDrops()
    {
        var leg = Assert.Single(Plan(Out("awgoff") with { Targets = ["geoip:ru"] }).Legs);

        Assert.Contains("ip daddr @r1v4 log prefix \"ag:h:1\" group 7317 drop", RouteRuleset.Lines(leg, Group));
    }

    [Fact]
    public void ABalancerLogsAfterItPicksTheMark()
    {
        var balancer = BalanceDefaults.Fresh("pick") with { Id = 1, Strategy = BalanceStrategy.Round, Members = ["awgbor", "direct"] };
        var plan = RoutePlan.Build(
            [Out("pick")],
            Ways,
            Index(),
            ["awg1"],
            null,
            [balancer],
            new HashSet<string>(["awgbor", "direct"], StringComparer.Ordinal));

        var line = Assert.Single(RouteRuleset.Lines(Assert.Single(plan.Legs), Group));

        Assert.Equal("meta mark set numgen inc mod 2 map { 0 : 0xa602, 1 : 0xa601 } log prefix \"ag:o:1\" group 7317 return", line);
    }

    [Fact]
    public void TheGuardsOfTheResolverLogWhatTheyDrop()
    {
        var lines = RouteRuleset.Guard(Resolver, Group);

        Assert.Equal(
            [
                "meta l4proto { tcp, udp } th dport 853 log prefix \"ag:g:dot\" group 7317 drop",
                "ip daddr @doh4 meta l4proto { tcp, udp } th dport 443 log prefix \"ag:g:doh\" group 7317 drop",
                "ip6 daddr @doh6 meta l4proto { tcp, udp } th dport 443 log prefix \"ag:g:doh\" group 7317 drop",
            ],
            lines);
        Assert.DoesNotContain(RouteRuleset.Guard(Resolver), line => line.Contains("log", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ag:o:12", AccessVerdict.Out, 12L, "")]
    [InlineData("ag:d:3", AccessVerdict.Host, 3L, "")]
    [InlineData("ag:b:-1", AccessVerdict.Block, -1L, "")]
    [InlineData("ag:h:4", AccessVerdict.Held, 4L, "")]
    [InlineData("ag:g:dot", AccessVerdict.Guard, null, "dot")]
    [InlineData("ag:g:doh", AccessVerdict.Guard, null, "doh")]
    [InlineData("ag:n", AccessVerdict.Host, null, "")]
    public void APrefixSaysHowTheConnectionWasDecided(string prefix, string verdict, long? rule, string guard)
    {
        Assert.Equal(new AccessDecision(verdict, rule, guard), AccessTag.Read(prefix));
    }

    [Theory]
    [InlineData("")]
    [InlineData("IN=eth0 ")]
    [InlineData("ag:x:1")]
    [InlineData("ag:o:one")]
    [InlineData("ag:g:dns")]
    [InlineData("ag:o:1:2")]
    public void APrefixOfSomeoneElseIsNotRead(string prefix) => Assert.Null(AccessTag.Read(prefix));

    [Fact]
    public void AnIpv4PacketGivesItsAddressesAndPort()
    {
        var flow = AccessFlow.Read(Four("10.8.1.2", "142.250.1.1", AccessFlow.Tcp, 443));

        Assert.Equal(new AccessFlow(IPAddress.Parse("10.8.1.2"), IPAddress.Parse("142.250.1.1"), AccessFlow.Tcp, 443, 51234), flow);
    }

    [Fact]
    public void AnIpv6PacketGivesItsAddressesAndPort()
    {
        var flow = AccessFlow.Read(Six("fd00::2", "2a00:1450::1", AccessFlow.Udp, 443));

        Assert.Equal(new AccessFlow(IPAddress.Parse("fd00::2"), IPAddress.Parse("2a00:1450::1"), AccessFlow.Udp, 443, 51234), flow);
    }

    [Fact]
    public void ALaterFragmentCarriesNoPort()
    {
        var packet = Four("10.8.1.2", "142.250.1.1", AccessFlow.Udp, 443);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), 0x0010);

        Assert.Equal(0, AccessFlow.Read(packet)?.Port);
    }

    [Fact]
    public void WhatIsNotAnIpPacketIsNotRead()
    {
        Assert.Null(AccessFlow.Read([]));
        Assert.Null(AccessFlow.Read([0x45, 0, 0]));
        Assert.Null(AccessFlow.Read(new byte[48]));
    }

    [Fact]
    public void TheBatchOfTheKernelGivesItsPackets()
    {
        var payload = Four("10.8.1.2", "142.250.1.1", AccessFlow.Tcp, 443);
        var batch = Message(
                0x0400,
                2,
                (10, Encoding.UTF8.GetBytes("ag:o:1\0")),
                (2, Big(0xA602)),
                (4, Big(7)),
                (9, payload))
            .Concat(Message(0x0003, 0))
            .Concat(Message(0x0400, 10, (10, Encoding.UTF8.GetBytes("ag:n\0"))))
            .ToArray();
        var packets = new List<NetfilterPacket>();

        NetfilterLog.Parse(batch, packets);

        Assert.Equal(2, packets.Count);
        Assert.Equal("ag:o:1", packets[0].Prefix);
        Assert.Equal(0xA602u, packets[0].Mark);
        Assert.Equal(7u, packets[0].Inbound);
        Assert.Equal(payload, packets[0].Payload);
        Assert.Equal((byte)2, packets[0].Family);
        Assert.Equal("ag:n", packets[1].Prefix);
        Assert.Empty(packets[1].Payload);
    }

    [Fact]
    public void AShortBatchStopsTheReading()
    {
        var batch = Message(0x0400, 2, (10, Encoding.UTF8.GetBytes("ag:n\0")));
        var packets = new List<NetfilterPacket>();

        NetfilterLog.Parse(batch.AsSpan(0, batch.Length - 4), packets);

        Assert.Empty(packets);
    }

    [Fact]
    public void TheRequestTakesTheGroupAndCopiesTheHeadOfEveryPacket()
    {
        var request = NetfilterLog.Config(5, Group, AccessDefaults.Snap);

        Assert.Equal((uint)request.Length, BinaryPrimitives.ReadUInt32LittleEndian(request));
        Assert.Equal((ushort)0x0401, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(4)));
        Assert.Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(6)));
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(8)));
        Assert.Equal(Group, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(18)));
        Assert.Equal((byte)1, request[24]);
        Assert.Equal((uint)AccessDefaults.Snap, BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(32)));
        Assert.Equal((byte)2, request[36]);
    }

    [Fact]
    public void ARecordNamesTheClientTheRuleAndTheWayOut()
    {
        var plan = Plan(Out("awgbor") with { Name = "youtube", Targets = ["geoip:ru"] });
        var names = new AccessNames { IsOn = true };
        names.Hear(IPAddress.Parse("10.8.1.2"), Answer("WWW.YouTube.com.", "77.88.8.1"));

        var record = Record(Packet("ag:o:1", 0xA602, Four("10.8.1.2", "77.88.8.1", AccessFlow.Tcp, 443)), plan, names);

        Assert.NotNull(record);
        Assert.Equal("anna", record.Client);
        Assert.Equal("10.8.1.2", record.Source);
        Assert.Equal("awg1", record.Inbound);
        Assert.Equal("77.88.8.1", record.Target);
        Assert.Equal(443, record.Port);
        Assert.Equal(AccessFlow.Tcp, record.Protocol);
        Assert.Equal("www.youtube.com", record.Name);
        Assert.Equal(AccessVerdict.Out, record.Verdict);
        Assert.Equal(1L, record.Rule);
        Assert.Equal("youtube", record.RuleName);
        Assert.Equal("awgbor", record.Way);
        Assert.Equal(string.Empty, record.Via);
    }

    [Fact]
    public void ARecordSaysWhetherTheConnectionLeftTheHostOrWasHandedOn()
    {
        var plan = Plan(Out("awgbor") with { Targets = ["geoip:ru"] });
        var packet = Four("10.8.1.2", "77.88.8.1", AccessFlow.Tcp, 443);

        var relay = Record(Packet("ag:o:1", 0xA602, packet), plan, new AccessNames());
        var local = Record(Packet("ag:o:1", 0xA601, packet), plan, new AccessNames());
        var host = Record(Packet("ag:n", 0, packet), plan, new AccessNames());
        var block = Record(Packet("ag:b:1", 0, packet), plan, new AccessNames());

        Assert.Equal(AccessPath.Relay, relay?.Path);
        Assert.Equal(51234, relay?.SourcePort);
        Assert.Equal(AccessPath.Local, local?.Path);
        Assert.Equal("direct", local?.Way);
        Assert.Equal(AccessPath.Local, host?.Path);
        Assert.Equal(string.Empty, block?.Path);
    }

    [Fact]
    public void ARecordOfABalancerNamesTheMemberItPicked()
    {
        var balancer = BalanceDefaults.Fresh("pick") with { Id = 1, Strategy = BalanceStrategy.Round, Members = ["awgbor", "direct"] };
        var plan = RoutePlan.Build(
            [Out("pick") with { Targets = ["geoip:ru"] }],
            Ways,
            Index(),
            ["awg1"],
            null,
            [balancer],
            new HashSet<string>(["awgbor", "direct"], StringComparer.Ordinal));

        var record = Record(Packet("ag:o:1", 0xA601, Four("10.8.1.2", "77.88.8.1", AccessFlow.Tcp, 443)), plan, new AccessNames());

        Assert.Equal("direct", record?.Way);
        Assert.Equal("pick", record?.Via);
    }

    [Fact]
    public void ARecordOfAHeldRuleNamesTheChannelItWaitsFor()
    {
        var plan = Plan(Out("awgoff") with { Targets = ["geoip:ru"] });

        var record = Record(Packet("ag:h:1", 0, Four("10.8.1.2", "77.88.8.1", AccessFlow.Tcp, 443)), plan, new AccessNames());

        Assert.Equal(AccessVerdict.Held, record?.Verdict);
        Assert.Equal("awgoff", record?.Way);
    }

    [Fact]
    public void ARecordOfAGuardNamesTheGuard()
    {
        var record = Record(Packet("ag:g:dot", 0, Four("10.8.1.2", "9.9.9.9", AccessFlow.Tcp, 853)), null, new AccessNames());

        Assert.Equal(AccessVerdict.Guard, record?.Verdict);
        Assert.Equal("dot", record?.Way);
        Assert.Null(record?.Rule);
    }

    [Fact]
    public void APacketTheRulesetDidNotLogGivesNoRecord()
    {
        Assert.Null(Record(Packet("IN=eth0", 0, Four("10.8.1.2", "9.9.9.9", AccessFlow.Tcp, 443)), null, new AccessNames()));
        Assert.Null(Record(Packet("ag:n", 0, [1, 2, 3]), null, new AccessNames()));
    }

    [Fact]
    public void AQuestionToTheResolverIsLeftOutOnlyWhileTheResolverRuns()
    {
        var record = new AccessRecord { Port = 53, Protocol = AccessFlow.Udp };
        var plan = Plan(Out("awgbor"));

        Assert.True(AccessReader.IsQuestion(record, plan with { Dns = Resolver }));
        Assert.False(AccessReader.IsQuestion(record, plan));
        Assert.False(AccessReader.IsQuestion(record with { Port = 443 }, plan with { Dns = Resolver }));
    }

    [Fact]
    public void TheFirstPacketBackOverTcpOnlySaysTheConnectionWasTaken()
    {
        var answer = AccessAnswer.Read(Back("142.250.1.1", "10.8.1.2", AccessFlow.Tcp, 443, 0x12));

        Assert.Equal(new AccessAnswer(new AccessKey(AccessFlow.Tcp, "10.8.1.2", 51234, "142.250.1.1", 443), null), answer);
    }

    [Theory]
    [InlineData(0x18, AccessOutcome.Ok)]
    [InlineData(0x19, AccessOutcome.Ok)]
    [InlineData(0x14, AccessOutcome.Reset)]
    [InlineData(0x11, AccessOutcome.Reset)]
    public void DataOrAResetSettlesATcpConnection(byte flags, string outcome) =>
        Assert.Equal(outcome, AccessAnswer.Read(Back("142.250.1.1", "10.8.1.2", AccessFlow.Tcp, 443, flags))?.Outcome);

    [Fact]
    public void AnyPacketBackSettlesUdp()
    {
        var answer = AccessAnswer.Read(Back("1.1.1.1", "10.8.1.2", AccessFlow.Udp, 53, 0));

        Assert.Equal(new AccessAnswer(new AccessKey(AccessFlow.Udp, "10.8.1.2", 51234, "1.1.1.1", 53), AccessOutcome.Ok), answer);
    }

    [Fact]
    public void AnErrorBackNamesTheConnectionItCarries()
    {
        var error = Control4("198.51.100.1", "10.8.1.2", 3, Four("10.8.1.2", "142.250.1.1", AccessFlow.Udp, 443));
        var expected = new AccessKey(AccessFlow.Udp, "10.8.1.2", 51234, "142.250.1.1", 443);

        Assert.Equal(new AccessAnswer(expected, AccessOutcome.Unreachable), AccessAnswer.Read(error));
    }

    [Fact]
    public void AnIpv6ErrorBackNamesTheConnectionItCarries()
    {
        var error = Control6("2001:db8::1", "fd00::2", 1, Six("fd00::2", "2a00:1450::1", AccessFlow.Tcp, 443));
        var expected = new AccessKey(AccessFlow.Tcp, "fd00::2", 51234, "2a00:1450::1", 443);

        Assert.Equal(new AccessAnswer(expected, AccessOutcome.Unreachable), AccessAnswer.Read(error));
    }

    [Fact]
    public void AnEchoReplySettlesAPing()
    {
        var reply = Control4("142.250.1.1", "10.8.1.2", 0, []);

        Assert.Equal(new AccessAnswer(new AccessKey(1, "10.8.1.2", 0, "142.250.1.1", 0), AccessOutcome.Ok), AccessAnswer.Read(reply));
    }

    [Fact]
    public void WhatTellsNothingAboutAConnectionIsNotAnAnswer()
    {
        Assert.Null(AccessAnswer.Read([]));
        Assert.Null(AccessAnswer.Read(Control4("198.51.100.1", "10.8.1.2", 5, [])));
        Assert.Null(AccessAnswer.Read(Back("142.250.1.1", "10.8.1.2", AccessFlow.Tcp, 443, 0x18)[..33]));
    }

    [Fact]
    public void AConnectionThatWentOutWaitsForWhatComesBack()
    {
        var watch = new AccessWatch();
        var record = Went(AccessVerdict.Out);

        watch.Take(record);
        var early = watch.Due(record.At.AddSeconds(1));
        watch.Hear(new AccessAnswer(AccessKey.Of(record), AccessOutcome.Ok), record.At.AddSeconds(1));

        Assert.Empty(early);
        Assert.Equal(AccessOutcome.Ok, Assert.Single(watch.Due(record.At.AddSeconds(1))).Outcome);
        Assert.Equal(0, watch.Count);
    }

    [Fact]
    public void NothingBackInTimeIsSilentAndATakenConnectionWithoutDataIsEmpty()
    {
        var watch = new AccessWatch();
        var silent = Went(AccessVerdict.Host);
        var taken = Went(AccessVerdict.Out) with { SourcePort = 40000 };

        watch.Take(silent);
        watch.Take(taken);
        watch.Hear(new AccessAnswer(AccessKey.Of(taken), null), taken.At.AddSeconds(9));
        var due = watch.Due(silent.At.AddSeconds(10));
        var later = watch.Due(taken.At.AddSeconds(19));

        Assert.Equal(AccessOutcome.Silent, Assert.Single(due).Outcome);
        Assert.Equal(AccessOutcome.Empty, Assert.Single(later).Outcome);
    }

    [Fact]
    public void AConnectionThePanelDroppedIsWrittenAtOnceWithoutAnOutcome()
    {
        var watch = new AccessWatch();

        watch.Take(Went(AccessVerdict.Block));

        Assert.Equal(string.Empty, Assert.Single(watch.Due(DateTimeOffset.UnixEpoch)).Outcome);
    }

    [Fact]
    public void AFullWatchWritesTheRecordAtOnceAndAStopLetsEveryRecordGo()
    {
        var watch = new AccessWatch(1);

        watch.Take(Went(AccessVerdict.Out));
        watch.Take(Went(AccessVerdict.Out) with { SourcePort = 40000 });

        Assert.Single(watch.Due(DateTimeOffset.UnixEpoch));
        Assert.Equal(1, watch.Count);
        Assert.Equal(string.Empty, Assert.Single(watch.Flush()).Outcome);
        Assert.Equal(0, watch.Count);
    }

    [Fact]
    public void AnAnswerToAConnectionNobodyWatchesIsLetGo()
    {
        var watch = new AccessWatch();
        var key = new AccessKey(AccessFlow.Tcp, "10.8.1.2", 1, "1.1.1.1", 443);

        watch.Hear(new AccessAnswer(key, AccessOutcome.Ok), DateTimeOffset.UnixEpoch);

        Assert.Empty(watch.Due(DateTimeOffset.UnixEpoch.AddHours(1)));
    }

    [Fact]
    public void AConnectionThatAsksAgainStaysOneRecordAndWaitsAfterItsLastAsking()
    {
        var watch = new AccessWatch();
        var first = Went(AccessVerdict.Host);

        watch.Take(first);
        watch.Take(first with { At = first.At.AddSeconds(3) });
        var early = watch.Due(first.At.AddSeconds(12));
        var written = watch.Due(first.At.AddSeconds(13));
        watch.Take(first with { At = first.At.AddSeconds(15) });

        Assert.Empty(early);
        var record = Assert.Single(written);
        Assert.Equal((first.At, AccessOutcome.Silent), (record.At, record.Outcome));
        Assert.Empty(watch.Due(first.At.AddMinutes(1)));
    }

    [Fact]
    public void AConnectionThatKeepsAskingIsWrittenThirtySecondsAfterItStarted()
    {
        var watch = new AccessWatch();
        var first = Went(AccessVerdict.Out);

        foreach (var second in new[] { 0, 5, 10, 15, 20, 25 })
        {
            watch.Take(first with { At = first.At.AddSeconds(second) });
        }

        Assert.Empty(watch.Due(first.At.AddSeconds(29)));
        Assert.Single(watch.Due(first.At.AddSeconds(30)));
    }

    [Fact]
    public void ADroppedConnectionIsWrittenOnceWhileItAsksAgainTheSameWay()
    {
        var watch = new AccessWatch();
        var first = Went(AccessVerdict.Block);

        watch.Take(first);
        watch.Take(first with { At = first.At.AddSeconds(1) });
        watch.Take(first with { At = first.At.AddSeconds(3) });
        var written = watch.Due(first.At.AddSeconds(3));
        watch.Take(first with { At = first.At.AddSeconds(4), Verdict = AccessVerdict.Host });

        Assert.Single(written);
        Assert.Equal(1, watch.Count);
    }

    [Fact]
    public void TheNameBookTakesNothingWhileTheLogIsOff()
    {
        var names = new AccessNames();

        names.Hear(IPAddress.Parse("10.8.1.2"), Answer("youtube.com", "77.88.8.1"));

        Assert.Equal(0, names.Count);
        Assert.Equal(string.Empty, names.Name(IPAddress.Parse("10.8.1.2"), IPAddress.Parse("77.88.8.1")));
    }

    [Fact]
    public void AClientGetsTheNameItAskedForAndAnotherTheLastOneAskedFor()
    {
        var names = new AccessNames { IsOn = true };

        names.Hear(IPAddress.Parse("10.8.1.2"), Answer("youtube.com", "77.88.8.1"));
        names.Hear(IPAddress.Parse("10.8.1.3"), Answer("ytimg.com", "77.88.8.1"));

        Assert.Equal("youtube.com", names.Name(IPAddress.Parse("10.8.1.2"), IPAddress.Parse("77.88.8.1")));
        Assert.Equal("ytimg.com", names.Name(IPAddress.Parse("10.8.1.4"), IPAddress.Parse("77.88.8.1")));
    }

    [Fact]
    public void TheNameBookForgetsTheOldestGenerationOnly()
    {
        var names = new AccessNames(3) { IsOn = true };
        var client = IPAddress.Parse("10.8.1.2");

        names.Hear(client, Answer("one.com", "1.1.1.1"));
        names.Hear(client, Answer("two.com", "2.2.2.2"));
        var one = names.Name(client, IPAddress.Parse("1.1.1.1"));
        names.Hear(client, Answer("three.com", "3.3.3.3"));
        names.Hear(client, Answer("four.com", "4.4.4.4"));
        names.Hear(client, Answer("five.com", "5.5.5.5"));

        Assert.Equal("one.com", one);
        Assert.Equal(string.Empty, names.Name(client, IPAddress.Parse("1.1.1.1")));
        Assert.Equal("five.com", names.Name(client, IPAddress.Parse("5.5.5.5")));
    }

    [Fact]
    public void TurningTheLogOffEmptiesTheNameBook()
    {
        var names = new AccessNames { IsOn = true };
        names.Hear(IPAddress.Parse("10.8.1.2"), Answer("youtube.com", "77.88.8.1"));

        names.IsOn = false;

        Assert.Equal(0, names.Count);
    }

    [Fact]
    public async Task TheResolverTellsTheLogWhoAskedAndWhatWasAnswered()
    {
        var heard = new List<(IPAddress? Client, string Name)>();
        var resolver = new DnsResolver(
            new Upstream("77.88.8.1"),
            new DnsCache(16),
            new DnsSets(new Ledger()),
            () => null,
            Resolver,
            new DnsState(),
            _ => Task.CompletedTask,
            (client, answer) => heard.Add((client, answer.Question)));

        var expected = new List<(IPAddress? Client, string Name)>
        {
            (IPAddress.Parse("10.8.1.2"), "www.youtube.com"),
            (null, "www.youtube.com"),
        };

        await resolver.AnswerAsync(DnsBuilder.Question(3, "www.youtube.com"), false, IPAddress.Parse("10.8.1.2"), CancellationToken.None);
        await resolver.AnswerAsync(DnsBuilder.Question(4, "www.youtube.com"), false, CancellationToken.None);

        Assert.Equal(expected, heard);
    }

    [Fact]
    public void AFullQueueDropsAndCountsInsteadOfWaiting()
    {
        var queue = new AccessQueue(2);
        var caught = new AccessCatch(DateTimeOffset.UnixEpoch, new NetfilterPacket(2, "ag:n", 0, 0, []));

        var taken = Enumerable.Range(0, 5).Select(_ => queue.Offer(caught)).ToArray();
        queue.Lose(4);

        Assert.Equal([true, true, false, false, false], taken);
        Assert.Equal(7, queue.Dropped);
        Assert.True(queue.Reader.TryRead(out _));
        Assert.True(queue.Reader.TryRead(out _));
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData("rr3---sn-4g5e6nsz.googlevideo.com", "googlevideo.com")]
    [InlineData("youtube.com", "youtube.com")]
    [InlineData("news.bbc.co.uk", "bbc.co.uk")]
    [InlineData("a.b.example.com.tr", "example.com.tr")]
    [InlineData("static.example.io", "example.io")]
    [InlineData("142.250.1.1", "142.250.1.1")]
    [InlineData("2a00:1450::1", "2a00:1450::1")]
    [InlineData("", "")]
    public void ANameBelongsToItsDomain(string name, string domain) => Assert.Equal(domain, AccessDomain.Of(name));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(90, true)]
    [InlineData(91, false)]
    public void ARecordIsKeptFromOneDayToThreeMonths(int days, bool taken) =>
        Assert.Equal(taken, new AccessSettings { Days = days }.Check() is null);

    private static AccessRecord? Record(NetfilterPacket packet, RoutePlan? plan, AccessNames names) =>
        AccessReader.Record(
            packet,
            DateTimeOffset.UnixEpoch,
            plan,
            address => address.Equals(IPAddress.Parse("10.8.1.2")) ? "anna" : string.Empty,
            index => index == 7 ? "awg1" : string.Empty,
            names);

    private static NetfilterPacket Packet(string prefix, uint mark, byte[] payload) => new(2, prefix, mark, 7, payload);

    private static AccessRecord Went(string verdict) => new()
    {
        At = DateTimeOffset.UnixEpoch,
        Source = "10.8.1.2",
        SourcePort = 51234,
        Target = "142.250.1.1",
        Port = 443,
        Protocol = AccessFlow.Tcp,
        Verdict = verdict,
    };

    private static DnsMessage Answer(string name, string address) =>
        new(1, true, 0, name, DnsRecordType.A, [new DnsRecord(name, DnsRecordType.A, 300, IPAddress.Parse(address))]);

    private static byte[] Four(string source, string target, int protocol, ushort port)
    {
        var packet = new byte[40];
        packet[0] = 0x45;
        packet[9] = (byte)protocol;
        IPAddress.Parse(source).GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse(target).GetAddressBytes().CopyTo(packet, 16);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(20), 51234);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(22), port);

        return packet;
    }

    private static byte[] Back(string source, string target, int protocol, ushort port, byte flags)
    {
        var packet = new byte[40];
        packet[0] = 0x45;
        packet[9] = (byte)protocol;
        IPAddress.Parse(source).GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse(target).GetAddressBytes().CopyTo(packet, 16);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(20), port);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(22), 51234);
        packet[33] = flags;

        return packet;
    }

    private static byte[] Control4(string source, string target, byte type, byte[] inner)
    {
        var packet = new byte[28 + inner.Length];
        packet[0] = 0x45;
        packet[9] = 1;
        IPAddress.Parse(source).GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse(target).GetAddressBytes().CopyTo(packet, 16);
        packet[20] = type;
        inner.CopyTo(packet, 28);

        return packet;
    }

    private static byte[] Control6(string source, string target, byte type, byte[] inner)
    {
        var packet = new byte[48 + inner.Length];
        packet[0] = 0x60;
        packet[6] = 58;
        IPAddress.Parse(source).GetAddressBytes().CopyTo(packet, 8);
        IPAddress.Parse(target).GetAddressBytes().CopyTo(packet, 24);
        packet[40] = type;
        inner.CopyTo(packet, 48);

        return packet;
    }

    private static byte[] Six(string source, string target, int protocol, ushort port)
    {
        var packet = new byte[48];
        packet[0] = 0x60;
        packet[6] = (byte)protocol;
        IPAddress.Parse(source).GetAddressBytes().CopyTo(packet, 8);
        IPAddress.Parse(target).GetAddressBytes().CopyTo(packet, 24);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(40), 51234);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(42), port);

        return packet;
    }

    private static byte[] Big(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);

        return bytes;
    }

    private static byte[] Message(ushort type, byte family, params (ushort Type, byte[] Value)[] attributes)
    {
        var body = new List<byte> { family, 0, 0, 0 };
        foreach (var (kind, value) in attributes)
        {
            var head = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(head, (ushort)(4 + value.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(2), kind);
            body.AddRange(head);
            body.AddRange(value);
            while (body.Count % 4 != 0)
            {
                body.Add(0);
            }
        }

        var message = new byte[16 + body.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)message.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4), type);
        body.CopyTo(message, 16);

        return message;
    }

    private static GeoIndex Index()
    {
        var files = new MemoryGeoFiles();
        files.Put("ip", Ip);
        files.Put("site", Site);

        return GeoIndex.Load(
            [
                new GeoSource { Name = "ip", Kind = GeoKind.Ip, Position = 1 },
                new GeoSource { Name = "site", Kind = GeoKind.Site, Position = 2 },
            ],
            files);
    }

    private static RoutePlan Plan(params RouteRule[] rules) => RoutePlan.Build(rules, Ways, Index(), ["awg1", "awg2"]);

    private static RouteRule Out(string outbound) =>
        RouteDefaults.Fresh("rule") with { Id = 1, Action = RouteAction.Out, Outbound = outbound };

    private sealed class Upstream(string address) : IDnsUpstream
    {
        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            var asked = DnsMessage.Read(question.Span)!;
            var told = new Told(asked.Question, asked.Type, 300, address);

            return Task.FromResult<byte[]?>(DnsBuilder.Answer(asked.Id, asked.Question, asked.Type, told));
        }
    }
}
