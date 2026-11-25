using System.Net;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public sealed class DnsRoutingTests
{
    private static readonly DnsSettings Settings = DnsDefaults.Settings;

    private static readonly OutboundConfig First = new() { Name = "first", Mark = 0xA601 };

    private static readonly OutboundConfig Second = new() { Name = "second", Mark = 0xA602 };

    [Theory]
    [InlineData("10.8.0.2", 700033915u)]
    [InlineData("10.8.0.3", 911941037u)]
    [InlineData("192.0.2.7", 677970960u)]
    [InlineData("2001:db8::2", 467714024u)]
    [InlineData("2001:db8::3", 485627123u)]
    [InlineData("fd00::1234", 563352609u)]
    public void StickyHashMatchesKernelReferenceVectors(string address, uint expected)
    {
        Assert.Equal(expected, DnsSticky.Hash(IPAddress.Parse(address).GetAddressBytes(), 2654435761u));
    }

    [Fact]
    public void TheRegisteredClientSuppliesTheInboundContext()
    {
        var plan = Plan([Rule() with { Inbounds = ["awg1"] }, Rule("second", 2)]) with
        {
            DnsOrigins = [new DnsOrigin(IPNetwork.Parse("10.8.0.2/32"), "awg1")],
        };
        using var routes = new DnsRoutes(() => plan);

        Assert.Equal(["9.9.9.9"], routes.Select("example.test", IPAddress.Parse("10.8.0.2")).Servers);
        Assert.Equal(["8.8.8.8"], routes.Select("example.test").Servers);
    }

    [Fact]
    public void AnAmbiguousClientAddressDoesNotGuessTheInbound()
    {
        var plan = Plan([Rule() with { Inbounds = ["awg1"] }, Rule("second", 2)]) with
        {
            DnsOrigins =
            [
                new DnsOrigin(IPNetwork.Parse("10.8.0.2/32"), "awg1"),
                new DnsOrigin(IPNetwork.Parse("10.8.0.2/32"), "awg2"),
            ],
        };
        using var routes = new DnsRoutes(() => plan);

        Assert.Equal(["8.8.8.8"], routes.Select("example.test", IPAddress.Parse("10.8.0.2")).Servers);
    }

    [Fact]
    public void ADirectEntryUsesItsRulesDnsThroughTheHost()
    {
        var selected = DnsRouting.Select(Plan([Rule() with { Action = RouteAction.Direct }]), "example.test");

        Assert.Equal(["9.9.9.9"], selected.Servers);
        Assert.Equal(0u, selected.Way.Mark);
    }

    [Theory]
    [InlineData(BalanceStrategy.Round)]
    [InlineData(BalanceStrategy.Sticky)]
    public void ABalancerUsesTheRulesDnsForItsSelectedMember(string strategy)
    {
        var balancer = new Balancer { Id = 1, Name = "mix", Strategy = strategy, Members = ["first", "second"] };
        var criterion = Criterion(1, "9.9.9.9") with
        {
            DnsMembers = new Dictionary<string, IReadOnlyList<string>> { ["second"] = ["8.8.8.8"] },
        };
        var plan = Plan([WithRules(Rule("mix"), criterion)], balancers: [balancer]);
        foreach (var turn in Enumerable.Range(0, 8))
        {
            var selected = DnsRouting.Select(plan, "example.test", IPAddress.Parse($"10.8.0.{turn + 2}"), turn: turn);
            Assert.Equal(selected.Way.Outbound == "second" ? ["8.8.8.8"] : new[] { "9.9.9.9" }, selected.Servers);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARenamedObjectKeepsResolvingFromTheUpdatedPlanWithoutRestart(bool balancer)
    {
        var name = balancer ? "mix" : "first";
        var plan = Plan([Rule(name)], balancers: [new Balancer { Name = "mix", Members = ["first", "second"] }]);
        using var routes = new DnsRoutes(() => plan, (servers, _) => new Book(servers[0]));
        Assert.Null(routes.Select("example.test").Way.Fault);
        var changed = plan.Legs[0].Rule with { Outbound = "renamed" };
        plan = balancer
            ? Plan([changed], balancers: [new Balancer { Name = "renamed", Members = ["first", "second"] }])
            : Plan([changed], [First with { Name = "renamed" }, Second]);

        var chosen = routes.Select("example.test");
        Assert.Null(chosen.Way.Fault);
        Assert.Equal(First.Mark, chosen.Way.Mark);
        Assert.Equal(["9.9.9.9"], chosen.Servers);
        Assert.NotNull(await routes.ForRule(new DnsWarmName(1, "example.test"))
            .AskAsync(DnsBuilder.Question(1, "example.test"), false, CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public void UnmatchedNamesAndMissingPlansAreClosed()
    {
        Assert.Equal("no-dns-rule", DnsRouting.Select(Plan([Rule()]), "other.test").Way.Fault?.Code);
        Assert.Equal("no-dns-rule", DnsRouting.Select(null, "example.test").Way.Fault?.Code);
    }

    [Fact]
    public void ABlockedEntryDoesNotSendDnsThroughAnotherEntry()
    {
        var plan = Plan([Rule() with { Action = RouteAction.Block }, Rule("second", 2)]);
        var selected = DnsRouting.Select(plan, "example.test");

        Assert.Equal("dns-blocked", selected.Way.Fault?.Code);
        Assert.Empty(selected.Servers);
        Assert.Null(selected.Way.Mark);
    }

    [Fact]
    public void TheFirstMatchingEntrySuppliesTheRuleAndPath()
    {
        var selected = DnsRouting.Select(Plan([Rule(), Rule("second", 2)]), "example.test");

        Assert.Equal(["9.9.9.9"], selected.Servers);
        Assert.Equal(First.Mark, selected.Way.Mark);
    }

    [Fact]
    public void ASelectedRuleWithoutDnsDoesNotContinueIntoAnotherEntry()
    {
        var empty = WithRules(Rule(), Criterion(1, "") with { Dns = [] });
        var selected = DnsRouting.Select(Plan([empty, Rule("second", 2)]), "example.test");

        Assert.Equal("no-rule-dns", selected.Way.Fault?.Code);
        Assert.Empty(selected.Servers);
        Assert.Null(selected.Way.Mark);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("inbound")]
    public void AConditionNotSatisfiedByTheDnsContextCannotSelectAnEntry(string condition)
    {
        var first = condition switch
        {
            "client" => Rule() with { Clients = ["anna"] },
            "inbound" => Rule() with { Inbounds = ["awg1"] },
            _ => Rule() with { Inbounds = ["awg1"] },
        };
        var clients = new[] { new TunnelClient { Name = "anna", Address = ["10.8.0.2/32"] } };
        var plan = Plan([first, Rule("second", 2)], clients: clients);

        Assert.Equal(["8.8.8.8"], DnsRouting.Select(plan, "example.test").Servers);
    }

    [Theory]
    [InlineData("port")]
    [InlineData("source-port")]
    [InlineData("protocol")]
    public void DnsIgnoresPortAndProtocolFiltersWhileTrafficKeepsThem(string condition)
    {
        var first = condition switch
        {
            "port" => Rule() with { Ports = ["443"] },
            "source-port" => Rule() with { SourcePorts = ["12345"] },
            _ => Rule() with { Protocol = RouteProtocol.Tcp },
        };
        var plan = Plan([first, Rule("second", 2)]);
        var dns = DnsRouting.Select(plan, "example.test");
        var traffic = RouteProbe.Test(plan, new RouteQuery
        {
            Name = "example.test",
            Addresses = [IPAddress.Parse("203.0.113.17")],
            Inbound = "awg1",
            Port = 80,
            SourcePort = 5555,
            Protocol = RouteProtocol.Udp,
        }, (_, _) => true);

        Assert.Equal(["9.9.9.9"], dns.Servers);
        Assert.Equal(First.Mark, dns.Way.Mark);
        Assert.Equal(2, traffic.Leg!.Rule.Id);
        Assert.Equal(Second.Mark, traffic.Leg.Mark);
    }

    [Theory]
    [InlineData("10.8.0.7", "10.8.0.2/24")]
    [InlineData("2001:db8::7", "2001:db8::2/64")]
    public void ClientAndInboundConditionsAreAppliedTogether(string address, string range)
    {
        var plan = Plan([Rule() with { Clients = ["anna"], Inbounds = ["awg1"] }, Rule("second", 2)],
            clients: [new TunnelClient { Name = "anna", Address = [range] }]);

        var chosen = DnsRouting.Select(plan, "example.test", IPAddress.Parse(address), "awg1");
        var different = DnsRouting.Select(plan, "example.test", IPAddress.Parse(address), "awg2");

        Assert.Equal(["9.9.9.9"], chosen.Servers);
        Assert.Equal(["8.8.8.8"], different.Servers);
    }

    [Fact]
    public void AnIpOnlyRuleCannotClaimAnUnresolvedName()
    {
        var first = WithRules(Rule(), Criterion(1, "9.9.9.9") with { Targets = ["192.0.2.0/24"] });

        Assert.Equal(["8.8.8.8"], DnsRouting.Select(Plan([first, Rule("second", 2)]), "example.test").Servers);
    }

    [Fact]
    public void DifferentReusableRulesSelectTheirOwnDnsOnOneEntry()
    {
        var video = Criterion(1, "4.4.4.4") with { Targets = ["video.test"] };
        var other = Criterion(2, "8.8.4.4");
        var entry = WithRules(Rule(), video, other);
        var plan = Plan([entry]);

        Assert.Equal(["4.4.4.4"], DnsRouting.Select(plan, "www.video.test").Servers);
        Assert.Equal(["8.8.4.4"], DnsRouting.Select(plan, "example.test").Servers);
        Assert.Equal(First.Mark, DnsRouting.Select(plan, "www.video.test").Way.Mark);
        Assert.Equal("no-dns-rule", DnsRouting.Select(plan, "unmatched.test").Way.Fault?.Code);
    }

    [Fact]
    public void OverlappingRulesFollowTheOrderOfTheEntryReferences()
    {
        var one = Criterion(1, "4.4.4.4");
        var two = Criterion(2, "8.8.4.4");

        Assert.Equal(two.Dns, DnsRouting.Select(Plan([WithRules(Rule(), two, one)]), "example.test").Servers);
    }

    [Fact]
    public void AnEmptyExplicitDnsRuleDoesNotSkipToAnotherMatchingRule()
    {
        var noDns = Criterion(1, "") with { Dns = [] };
        var fallback = Criterion(2, "4.4.4.4");

        Assert.Equal("no-rule-dns", DnsRouting.Select(Plan([WithRules(Rule(), noDns, fallback)]), "example.test").Way.Fault?.Code);
    }

    [Fact]
    public void AnExplicitCatchAllRuleMatchesNamesOutsideTheOtherRules()
    {
        var named = Criterion(1, "4.4.4.4");
        var fallback = Criterion(2, "8.8.4.4") with { Targets = [] };
        var plan = Plan([WithRules(Rule(), named, fallback)]);

        Assert.False(plan.Legs[0].Rule.HasTargets);
        Assert.Equal(named.Dns, DnsRouting.Select(plan, "example.test").Servers);
        Assert.Equal(fallback.Dns, DnsRouting.Select(plan, "different.test").Servers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANamedRuleWithACatchAllKeepsDnsPoliciesAndMatchesEveryDestination(bool byClient)
    {
        var named = Criterion(1, "9.9.9.9");
        var fallback = Criterion(2, "8.8.8.8") with { Targets = [] };
        var entry = WithRules(Rule(), named, fallback) with { Clients = byClient ? ["anna"] : [] };
        var plan = Plan([entry], clients: [new TunnelClient { Name = "anna", Address = ["10.8.0.2/32", "fd00::2/128"] }]);
        var leg = plan.Legs[0];
        var lines = RouteRuleset.Lines(leg);

        Assert.True(leg.IsLive);
        Assert.False(leg.Rule.HasTargets);
        Assert.True(DnsNeed.Of(plan, false));
        Assert.Equal(byClient ? 2 : 1, lines.Count(line => !line.Contains(" daddr ", StringComparison.Ordinal)));
        foreach (var address in new[] { "203.0.113.71", "2001:db8::71" })
        {
            var verdict = RouteProbe.Test(plan, new RouteQuery
            {
                Addresses = [IPAddress.Parse(address)],
                Sources = [IPAddress.Parse("10.8.0.2"), IPAddress.Parse("fd00::2")],
                Inbound = "awg1",
                Port = 1234,
            }, (_, _) => false);

            Assert.Equal(RouteProbe.Out, verdict.Verdict);
            Assert.Equal(entry.Id, verdict.Leg!.Rule.Id);
        }
        Assert.Equal(named.Dns, DnsRouting.Select(plan, "example.test", IPAddress.Parse("10.8.0.2")).Servers);
        Assert.Equal(fallback.Dns, DnsRouting.Select(plan, "outside.test", IPAddress.Parse("10.8.0.2")).Servers);
    }

    [Fact]
    public void AnEntryKeepsSixtyFourMigratedRulesAndItsDnsFallback()
    {
        var rules = Enumerable.Range(1, 64).Select(id => Criterion(id, "9.9.9.9") with
        {
            Targets = [$"part{id}.test"],
        }).Append(Criterion(65, "8.8.8.8") with { Targets = [] }).ToArray();
        var plan = Plan([WithRules(Rule(), rules)]);

        Assert.True(plan.Legs[0].IsLive);
        Assert.Equal(["9.9.9.9"], DnsRouting.Select(plan, "part64.test").Servers);
        Assert.Equal(["8.8.8.8"], DnsRouting.Select(plan, "outside.test").Servers);
    }

    [Fact]
    public void CombinedReusableRulesKeepMoreThanOneLegacyGroupOfTargets()
    {
        var one = Criterion(1, "9.9.9.9") with
        {
            Targets = [.. Enumerable.Range(0, 600).Select(id => $"one{id}.test")],
        };
        var two = Criterion(2, "8.8.8.8") with
        {
            Targets = [.. Enumerable.Range(0, 600).Select(id => $"two{id}.test")],
        };
        var plan = Plan([WithRules(Rule(), one, two)]);

        Assert.True(plan.Legs[0].IsLive);
        Assert.Equal(1200, plan.Legs[0].Domains.Count);
        Assert.Equal(two.Dns, DnsRouting.Select(plan, "two599.test").Servers);
    }

    [Fact]
    public void EveryPreviouslyAcceptedResolverAddressIsRetained()
    {
        var servers = Enumerable.Range(1, 12).Select(id => $"192.0.2.{id}").ToArray();
        var criterion = Criterion(1, "") with { Dns = servers };
        var plan = Plan([WithRules(Rule(), criterion)]);

        Assert.True(plan.Legs[0].IsLive);
        Assert.Equal(servers, DnsRouting.Select(plan, "example.test").Servers);
        Assert.Equal("bad-dns", RouteRules.Check(criterion with { Dns = [.. servers, "unresolved.test"] })?.Code);
    }

    [Fact]
    public void AReusableDefaultRouteInheritsTheServerOfEachHandlingEntry()
    {
        var route = Criterion(10, "4.4.4.4") with { DnsMode = TrafficDnsMode.Default };
        var servers = new[]
        {
            First with { Kind = OutboundKind.Wg, Address = ["10.44.0.1/32"], Dns = ["9.9.9.9"] },
            Second with { Kind = OutboundKind.Wg, Address = ["10.45.0.1/32"], Dns = ["8.8.8.8"] },
        };
        foreach (var server in servers)
        {
            var selected = DnsRouting.Select(Plan([WithRules(Rule(server.Name), route)], servers), "example.test");

            Assert.Equal(server.Dns, selected.Servers);
            Assert.Equal(server.Mark, selected.Way.Mark);
            Assert.Equal(server.Name, selected.Device);
            Assert.Equal(server.Address, selected.SourceAddresses);
        }
    }

    [Fact]
    public void ADefaultBalancerUsesItsOwnServersAndTheSelectedMembersPath()
    {
        var route = Criterion(1, "") with { DnsMode = TrafficDnsMode.Default, Dns = [] };
        var balancer = new Balancer { Name = "mix", Members = ["first", "second"], Strategy = BalanceStrategy.Round, Dns = ["4.4.4.4"] };
        var servers = new[]
        {
            First with { Kind = OutboundKind.Wg, Address = ["10.44.0.1/32"], Dns = ["9.9.9.9"] },
            Second with { Kind = OutboundKind.Wg, Address = ["10.45.0.1/32"], Dns = ["8.8.8.8"] },
        };
        var plan = Plan([WithRules(Rule("mix"), route)], servers, [balancer]);

        foreach (var turn in new[] { 0, 1 })
        {
            var selected = DnsRouting.Select(plan, "example.test", turn: turn);

            Assert.Equal(balancer.Dns, selected.Servers);
            Assert.Equal(servers[turn].Name, selected.Device);
            Assert.Equal(servers[turn].Address, selected.SourceAddresses);
        }
    }

    [Fact]
    public void ADefaultBalancerWithoutAnOverrideFollowsItsLiveMembersDns()
    {
        var route = Criterion(1, "") with { DnsMode = TrafficDnsMode.Default, Dns = [] };
        var balancer = new Balancer { Name = "mix", Members = ["first", "second"] };
        var servers = new[] { First with { Dns = ["9.9.9.9"] }, Second with { Dns = ["8.8.8.8"] } };
        var plan = Plan([WithRules(Rule("mix"), route)], servers, [balancer]);
        var before = DnsRouting.Select(plan, "example.test");
        var after = DnsRouting.Select(plan with { Ways = plan.Ways with { Alive = new HashSet<string> { "second" } } }, "example.test");

        Assert.Equal(servers[0].Dns, before.Servers);
        Assert.Equal(servers[1].Dns, after.Servers);
        Assert.NotEqual(before.Key, after.Key);
        Assert.Equal("no-live-member", DnsRouting.Select(plan with { Ways = plan.Ways with { Alive = new HashSet<string>() } }, "example.test").Way.Fault?.Code);
    }

    [Fact]
    public void DirectDefaultDnsUsesTheActualHostServersOrCloses()
    {
        var route = Criterion(1, "4.4.4.4") with { DnsMode = TrafficDnsMode.Default };
        var plan = Plan([WithRules(Rule() with { Action = RouteAction.Direct }, route)]);

        Assert.Equal("no-default-dns", DnsRouting.Select(plan, "example.test").Way.Fault?.Code);
        var selected = DnsRouting.Select(plan with { HostDns = ["192.0.2.53"] }, "example.test");
        Assert.Equal(["192.0.2.53"], selected.Servers);
        Assert.Equal(0u, selected.Way.Mark);
        Assert.Empty(selected.Device);
        Assert.Empty(selected.SourceAddresses);
    }

    [Fact]
    public void ALocalBalancerMemberUsesHostDnsWithoutTunnelBinding()
    {
        var route = Criterion(1, "") with { DnsMode = TrafficDnsMode.Default, Dns = [] };
        var balancer = new Balancer { Name = "mix", Members = ["first"] };
        var plan = Plan([WithRules(Rule("mix"), route)], balancers: [balancer]) with { HostDns = ["192.0.2.53"] };
        var selected = DnsRouting.Select(plan, "example.test");

        Assert.Equal(plan.HostDns, selected.Servers);
        Assert.Equal(First.Mark, selected.Way.Mark);
        Assert.Empty(selected.Device);
    }

    [Fact]
    public void MissingDefaultDnsCannotSelectTheNextMatchingRoute()
    {
        var route = Criterion(1, "") with { DnsMode = TrafficDnsMode.Default, Dns = [] };
        var later = Criterion(2, "4.4.4.4");
        var server = First with { Kind = OutboundKind.Wg, Address = ["10.44.0.1/32"] };
        var plan = Plan([WithRules(Rule(), route, later)], [server]);
        var selected = DnsRouting.Select(plan, "example.test");

        Assert.Equal("no-default-dns", selected.Way.Fault?.Code);
        Assert.Empty(selected.Servers);
        Assert.Null(selected.Way.Mark);
    }

    [Fact]
    public void CustomDnsOverridesServerDefaultsAndKeepsTheSameInterface()
    {
        var server = First with { Kind = OutboundKind.Wg, Address = ["10.44.0.1/32"], Dns = ["1.1.1.1"] };
        var selected = DnsRouting.Select(Plan([WithRules(Rule(), Criterion(1, "9.9.9.9"))], [server]), "example.test");

        Assert.Equal(["9.9.9.9"], selected.Servers);
        Assert.Equal(server.Name, selected.Device);
        Assert.Equal(server.Address, selected.SourceAddresses);
        Assert.Equal("bad-dns-mode", RouteRules.Check(Criterion(2, "9.9.9.9") with { DnsMode = "preset" })?.Code);
    }

    [Fact]
    public void AChangedTunnelAddressChangesTheDnsConnectionScope()
    {
        var server = First with { Kind = OutboundKind.Wg, Address = ["10.44.0.1/32"] };
        var entry = Rule();
        var before = DnsRouting.Select(Plan([entry], [server]), "example.test");
        var after = DnsRouting.Select(Plan([entry], [server with { Address = ["10.44.0.2/32"] }]), "example.test");

        Assert.NotEqual(before.Key, after.Key);
    }

    [Fact]
    public void ADeadSelectedServerDoesNotFallBackToTheHost()
    {
        var plan = Plan([Rule(), Rule("second", 2)], alive: new HashSet<string> { "second" });
        var chosen = DnsRouting.Select(plan, "example.test");

        Assert.Equal("outbound-down", chosen.Way.Fault?.Code);
        Assert.Empty(chosen.Servers);
    }

    [Fact]
    public void ABalancerFailoverChangesTheRuleMemberServersAndTheCacheScope()
    {
        var balancer = new Balancer { Id = 1, Name = "mix", Members = ["first", "second"] };
        var criterion = Criterion(1, "9.9.9.9") with
        {
            DnsMembers = new Dictionary<string, IReadOnlyList<string>> { ["second"] = ["8.8.8.8"] },
        };
        var plan = Plan([WithRules(Rule("mix"), criterion)], balancers: [balancer]);
        var before = DnsRouting.Select(plan, "example.test");
        var after = DnsRouting.Select(plan with { Ways = plan.Ways with { Alive = new HashSet<string> { "second" } } }, "example.test");

        Assert.Equal(["9.9.9.9"], before.Servers);
        Assert.Equal(["8.8.8.8"], after.Servers);
        Assert.NotEqual(before.Key, after.Key);
    }

    [Fact]
    public async Task TheResolverSeparatesCachedAnswersWhenTheRouteOrRuleServersChange()
    {
        var plan = Plan([Rule()]);
        var calls = new List<(string Server, uint Mark)>();
        using var routes = new DnsRoutes(() => plan, (servers, mark) => new Book(servers[0], () => calls.Add((servers[0], mark))));
        var fallback = new Book("192.0.2.1");
        var resolver = new DnsResolver(fallback, new DnsCache(16), new DnsSets(new Ledger()), () => plan,
            Settings, new DnsState(), _ => Task.CompletedTask, routes: routes);
        var question = DnsBuilder.Question(1, "example.test");

        Assert.Equal("9.9.9.9", Address(await resolver.AnswerAsync(question, false, CancellationToken.None).ConfigureAwait(false)));
        await resolver.AnswerAsync(question, false, CancellationToken.None).ConfigureAwait(false);
        Assert.Single(calls);
        plan = Plan([Rule("second")]);
        Assert.Equal("8.8.8.8", Address(await resolver.AnswerAsync(question, false, CancellationToken.None).ConfigureAwait(false)));
        plan = Plan([WithRules(Rule("second"), Criterion(1, "4.4.4.4"))]);
        Assert.Equal("4.4.4.4", Address(await resolver.AnswerAsync(question, false, CancellationToken.None).ConfigureAwait(false)));
        Assert.Equal(3, calls.Count);
        Assert.Equal(0, fallback.Calls);
        plan = plan with { Ways = plan.Ways with { Alive = new HashSet<string>() } };
        Assert.Equal(DnsMessage.ServerFailure, DnsMessage.Read((await resolver.AnswerAsync(question, false, CancellationToken.None).ConfigureAwait(false))!)!.Code);
        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public async Task TheResolverScopesStandInsByTheEffectiveDnsPolicy()
    {
        var plan = Plan([Rule()]);
        var standIns = new DnsStandIns();
        using var routes = new DnsRoutes(() => plan, (servers, _) => new Book(servers[0]));
        var names = new DomainMatcher([new GeoDomain(GeoDomainKind.Domain, "example.test")]);
        var resolver = new DnsResolver(new Book("192.0.2.1"), new DnsCache(16), new DnsSets(new Ledger()), () => plan,
            Settings, new DnsState(), _ => Task.CompletedTask, standIns: standIns, standing: _ => names, routes: routes);
        var question = DnsBuilder.Question(1, "example.test");
        var first = IPAddress.Parse(Address(await resolver.AnswerAsync(question, false, IPAddress.Parse("10.8.0.2"), CancellationToken.None).ConfigureAwait(false)));
        plan = Plan([Rule("second")]);
        var second = IPAddress.Parse(Address(await resolver.AnswerAsync(question, false, IPAddress.Parse("10.8.0.3"), CancellationToken.None).ConfigureAwait(false)));

        Assert.NotEqual(first, second);
        Assert.Equal("9.9.9.9", standIns.Real(first)!.ToString());
        Assert.Equal("8.8.8.8", standIns.Real(second)!.ToString());
    }

    [Fact]
    public async Task WarmingUsesThePolicyOfEachEntry()
    {
        var plan = Plan([Rule(), Rule("second", 2)]);
        var calls = new List<string>();
        using var routes = new DnsRoutes(() => plan, (servers, _) => new Book(servers[0], () => calls.Add(servers[0])));
        var fallback = new Book("192.0.2.1");

        await DnsWarm.AskAsync(fallback, [new DnsWarmName(1, "example.test"), new DnsWarmName(2, "example.test")],
            CancellationToken.None, routes.ForRule).ConfigureAwait(false);

        Assert.Contains("9.9.9.9", calls);
        Assert.Contains("8.8.8.8", calls);
        Assert.Equal(0, fallback.Calls);
        Assert.Null(await routes.ForRule(new DnsWarmName(999, "example.test"))
            .AskAsync(DnsBuilder.Question(1, "example.test"), false, CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task BusyPoolsRemainAliveDuringEvictionAndDisposal()
    {
        var books = new List<WaitingBook>();
        var routes = new DnsRoutes(() => null, (_, _) =>
        {
            var book = new WaitingBook();
            books.Add(book);

            return book;
        });
        var question = DnsBuilder.Question(1, "example.test");
        var pending = Enumerable.Range(1, DnsRoutes.MaxPools).Select(index => routes.AskAsync(
            new DnsResolution(["1.1.1.1"], new DnsWay((uint)index, index.ToString(), null)), question, false, CancellationToken.None)).ToArray();

        Assert.Null(await routes.AskAsync(new DnsResolution(["8.8.8.8"], DnsWay.Host), question, false, CancellationToken.None).ConfigureAwait(false));
        routes.Dispose();
        Assert.All(books, book => Assert.False(book.Disposed));
        foreach (var book in books)
        {
            book.Answer.SetResult(null);
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
        Assert.All(books, book => Assert.True(book.Disposed));
    }

    private static string Address(byte[]? answer) => DnsMessage.Read(answer!)!.Addresses.Single().ToString();

    private static TrafficRule Criterion(long id, string server) => new()
    {
        Id = id,
        Name = "rule-" + id,
        Targets = ["example.test"],
        Dns = [server],
    };

    private static RouteRule Rule(string outbound = "first", long id = 1) => WithRules(new RouteRule
    {
        Id = id,
        Name = "entry-" + id,
        Position = (int)id,
        Outbound = outbound,
    }, Criterion(id, outbound == "second" ? "8.8.8.8" : "9.9.9.9"));

    private static RouteRule WithRules(RouteRule entry, params TrafficRule[] rules) => entry with
    {
        RuleIds = [.. rules.Select(rule => rule.Id)],
        Criteria = rules,
    };

    private static RoutePlan Plan(IReadOnlyList<RouteRule> rules, IReadOnlyList<OutboundConfig>? outbounds = null,
        IReadOnlyList<Balancer>? balancers = null, IReadOnlyList<TunnelClient>? clients = null, IReadOnlySet<string>? alive = null) =>
        RoutePlan.Build(rules, outbounds ?? [First, Second], GeoIndex.Load([], new MemoryGeoFiles()), ["awg1", "awg2"],
            Settings, balancers, alive, clients);

    private sealed class Book(string address, Action? called = null) : IDnsUpstream
    {
        public int Calls { get; private set; }

        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            Calls++;
            called?.Invoke();
            var asked = DnsMessage.Read(question.Span)!;
            var answers = asked.Type == DnsRecordType.A
                ? new[] { new Told(asked.Question, asked.Type, 300, address) }
                : [];

            return Task.FromResult<byte[]?>(DnsBuilder.Answer(asked.Id, asked.Question, asked.Type, answers));
        }
    }

    private sealed class WaitingBook : IDnsUpstream, IDisposable
    {
        public TaskCompletionSource<byte[]?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct) => Answer.Task;

        public void Dispose() => Disposed = true;
    }
}
