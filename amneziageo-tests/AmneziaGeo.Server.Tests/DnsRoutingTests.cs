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
    private static readonly DnsSettings Settings = DnsDefaults.Settings with
    {
        Upstreams = ["1.1.1.1"],
        Selection = DnsSelection.Routing,
    };

    private static readonly OutboundConfig First = new() { Name = "first", Mark = 0xA601, Dns = ["9.9.9.9"] };

    private static readonly OutboundConfig Second = new() { Name = "second", Mark = 0xA602, Dns = ["8.8.8.8"] };

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
        var plan = Plan([Rule() with { Inbounds = ["awg1"] }, Rule() with { Id = 2, Position = 2, Outbound = "second" }]) with
        {
            DnsOrigins = [new DnsOrigin(IPNetwork.Parse("10.8.0.2/32"), "awg1")],
        };
        using var routes = new DnsRoutes(() => plan, Settings);

        Assert.Equal(First.Dns, routes.Select("example.test", IPAddress.Parse("10.8.0.2")).Servers);
        Assert.Equal(Second.Dns, routes.Select("example.test").Servers);
    }

    [Fact]
    public void AnAmbiguousClientAddressDoesNotGuessTheInbound()
    {
        var plan = Plan([Rule() with { Inbounds = ["awg1"] }, Rule() with { Id = 2, Position = 2, Outbound = "second" }]) with
        {
            DnsOrigins =
            [
                new DnsOrigin(IPNetwork.Parse("10.8.0.2/32"), "awg1"),
                new DnsOrigin(IPNetwork.Parse("10.8.0.2/32"), "awg2"),
            ],
        };
        using var routes = new DnsRoutes(() => plan, Settings);

        Assert.Equal(Second.Dns, routes.Select("example.test", IPAddress.Parse("10.8.0.2")).Servers);
    }

    [Fact]
    public void ADirectRuleUsesGlobalServersThroughTheHost()
    {
        var selected = DnsRouting.Select(Settings with { Outbound = "second" },
            Plan([Rule() with { Action = RouteAction.Direct }]), "example.test");

        Assert.Equal(Settings.Upstreams, selected.Servers);
        Assert.Equal(0u, selected.Way.Mark);
    }

    [Theory]
    [InlineData(BalanceStrategy.Round)]
    [InlineData(BalanceStrategy.Sticky)]
    public void ABalancerUsesTheDnsOfTheMemberItSelected(string strategy)
    {
        var balancer = new Balancer { Id = 1, Name = "mix", Strategy = strategy, Members = ["first", "second"] };
        var plan = Plan([Rule() with { Outbound = "mix" }], balancers: [balancer]);
        foreach (var turn in Enumerable.Range(0, 8))
        {
            var selected = DnsRouting.Select(Settings, plan, "example.test", IPAddress.Parse($"10.8.0.{turn + 2}"), turn: turn);
            Assert.Equal(plan.Ways.Outbound(selected.Way.Outbound)!.Dns, selected.Servers);
        }
    }

    [Fact]
    public async Task ARenamedDirectionKeepsResolvingWithoutRestart()
    {
        await CheckRename(DnsSelection.Outbound, false).ConfigureAwait(false);
        await CheckRename(DnsSelection.Routing, false).ConfigureAwait(false);
        await CheckRename(DnsSelection.Outbound, true).ConfigureAwait(false);
        await CheckRename(DnsSelection.Routing, true).ConfigureAwait(false);
    }

    private static async Task CheckRename(string selection, bool balancer)
    {
        var name = balancer ? "mix" : "first";
        var settings = Settings with { Selection = selection, Outbound = name };
        var plan = Plan([], balancers: [new Balancer { Name = "mix", Members = ["first", "second"] }]);
        using var routes = new DnsRoutes(() => plan, settings, (servers, _) => new Book(servers[0]));
        Assert.Null(routes.Select("example.test").Way.Fault);
        plan = balancer
            ? plan with { Ways = plan.Ways with { Balancers = [new Balancer { Name = "renamed", Members = ["first", "second"] }] } }
            : plan with { Ways = plan.Ways with { Outbounds = [First with { Name = "renamed" }, Second] } };

        routes.Follow(name, "renamed");

        var chosen = routes.Select("example.test");
        Assert.Null(chosen.Way.Fault);
        Assert.Equal(First.Mark, chosen.Way.Mark);
        Assert.Equal(selection == DnsSelection.Outbound ? First.Dns : Settings.Upstreams, chosen.Servers);
        Assert.NotNull(await routes.ForRule(new DnsWarmName(999, "example.test"))
            .AskAsync(DnsBuilder.Question(1, "example.test"), false, CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public void ExistingSettingsKeepGlobalServersAndTheGlobalRoute()
    {
        var selected = DnsRouting.Select(Settings with { Selection = DnsSelection.Global, Outbound = "second" },
            Plan([Rule()]), "example.test");

        Assert.Equal(["1.1.1.1"], selected.Servers);
        Assert.Equal(Second.Mark, selected.Way.Mark);
    }

    [Fact]
    public void AnExplicitDirectionSuppliesItsServersAndItsRoute()
    {
        var selected = DnsRouting.Select(Settings with { Selection = DnsSelection.Outbound, Outbound = "second" },
            Plan([Rule()]), "example.test");

        Assert.Equal(Second.Dns, selected.Servers);
        Assert.Equal(Second.Mark, selected.Way.Mark);
    }

    [Fact]
    public void AnExplicitDirectionWithoutDnsDoesNotUseGlobalServers()
    {
        var selected = DnsRouting.Select(Settings with { Selection = DnsSelection.Outbound, Outbound = "first" },
            Plan([Rule()], [First with { Dns = [] }, Second]), "example.test");

        Assert.Empty(selected.Servers);
        Assert.Null(selected.Way.Mark);
        Assert.Equal("no-outbound-dns", selected.Way.Fault?.Code);
    }

    [Fact]
    public void TheFirstMatchingRuleSuppliesTheDirectionDns()
    {
        var selected = DnsRouting.Select(Settings, Plan([Rule(), Rule() with { Id = 2, Position = 2, Outbound = "second" }]), "example.test");

        Assert.Equal(First.Dns, selected.Servers);
        Assert.Equal(First.Mark, selected.Way.Mark);
    }

    [Fact]
    public void InheritedServersUseTheMatchingRouteWithoutContinuingIntoLaterRules()
    {
        var plan = Plan([Rule(), Rule() with { Id = 2, Position = 2, Outbound = "second" }], [First with { Dns = [] }, Second]);
        var selected = DnsRouting.Select(Settings, plan, "example.test");

        Assert.Equal(Settings.Upstreams, selected.Servers);
        Assert.Equal(First.Mark, selected.Way.Mark);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("inbound")]
    [InlineData("port")]
    [InlineData("source-port")]
    [InlineData("protocol")]
    public void AConditionNotSatisfiedByTheDnsContextCannotSelectADirection(string condition)
    {
        var first = condition switch
        {
            "client" => Rule() with { Clients = ["anna"] },
            "inbound" => Rule() with { Inbounds = ["awg1"] },
            "port" => Rule() with { Ports = ["443"] },
            "source-port" => Rule() with { SourcePorts = ["12345"] },
            _ => Rule() with { Protocol = RouteProtocol.Tcp },
        };
        var clients = new[] { new TunnelClient { Name = "anna", Address = ["10.8.0.2/32"] } };
        var plan = Plan([first, Rule() with { Id = 2, Position = 2, Outbound = "second" }], clients: clients);

        var selected = DnsRouting.Select(Settings, plan, "example.test");

        Assert.Equal(Second.Dns, selected.Servers);
    }

    [Theory]
    [InlineData("10.8.0.7", "10.8.0.2/24")]
    [InlineData("2001:db8::7", "2001:db8::2/64")]
    public void ClientAndInboundConditionsAreAppliedTogether(string address, string range)
    {
        var plan = Plan([Rule() with { Clients = ["anna"], Inbounds = ["awg1"] },
            Rule() with { Id = 2, Position = 2, Outbound = "second" }],
            clients: [new TunnelClient { Name = "anna", Address = [range] }]);

        var chosen = DnsRouting.Select(Settings, plan, "example.test", IPAddress.Parse(address), "awg1");
        var different = DnsRouting.Select(Settings, plan, "example.test", IPAddress.Parse(address), "awg2");

        Assert.Equal(First.Dns, chosen.Servers);
        Assert.Equal(Second.Dns, different.Servers);
    }

    [Fact]
    public void AnIpOnlyRuleCannotClaimAnUnresolvedName()
    {
        var plan = Plan([Rule() with { Targets = ["192.0.2.0/24"] }, Rule() with { Id = 2, Position = 2, Outbound = "second" }]);

        Assert.Equal(Second.Dns, DnsRouting.Select(Settings, plan, "example.test").Servers);
    }

    [Fact]
    public void AGroupOverridesTheBalancerAndTheSelectedMemberOnlyForItsOwnNames()
    {
        var group = new AddressGroup { Id = 1, Name = "video", Targets = ["video.test"], Dns = ["4.4.4.4"] };
        var balancer = new Balancer { Id = 1, Name = "mix", Members = ["first", "second"], Dns = ["8.8.4.4"] };
        var rule = Rule() with { Outbound = "mix", Groups = [1] };
        var plan = Plan([rule], balancers: [balancer], groups: [group]);

        Assert.Equal(group.Dns, DnsRouting.Select(Settings, plan, "www.video.test").Servers);
        Assert.Equal(balancer.Dns, DnsRouting.Select(Settings, plan, "example.test").Servers);
        Assert.Equal(First.Mark, DnsRouting.Select(Settings, plan, "www.video.test").Way.Mark);

        var inherited = Plan([rule], balancers: [balancer with { Dns = [] }], groups: [group]);
        Assert.Equal(First.Dns, DnsRouting.Select(Settings, inherited, "example.test").Servers);
    }

    [Fact]
    public void MatchingGroupOverridesFollowTheOrderOfTheRuleReferences()
    {
        var groups = new[]
        {
            new AddressGroup { Id = 1, Name = "one", Targets = ["example.test"], Dns = ["4.4.4.4"] },
            new AddressGroup { Id = 2, Name = "two", Targets = ["example.test"], Dns = ["8.8.4.4"] },
        };

        Assert.Equal(groups[1].Dns, DnsRouting.Select(Settings, Plan([Rule() with { Groups = [2, 1] }], groups: groups), "example.test").Servers);
    }

    [Fact]
    public void ADeadSelectedDirectionDoesNotFallBackToTheHost()
    {
        var plan = Plan([Rule()], alive: new HashSet<string> { "second" });
        var chosen = DnsRouting.Select(Settings, plan, "example.test");

        Assert.NotNull(chosen.Way.Fault);
        Assert.Empty(chosen.Servers);
    }

    [Fact]
    public void ABalancerFailoverChangesTheServersAndTheCacheScope()
    {
        var balancer = new Balancer { Id = 1, Name = "mix", Members = ["first", "second"] };
        var plan = Plan([Rule() with { Outbound = "mix" }], balancers: [balancer]);
        var before = DnsRouting.Select(Settings, plan, "example.test");
        var after = DnsRouting.Select(Settings, plan with { Ways = plan.Ways with { Alive = new HashSet<string> { "second" } } }, "example.test");

        Assert.Equal(First.Dns, before.Servers);
        Assert.Equal(Second.Dns, after.Servers);
        Assert.NotEqual(before.Key, after.Key);
    }

    [Fact]
    public async Task TheResolverSeparatesCachedAnswersWhenTheRouteOrServersChange()
    {
        var plan = Plan([Rule()]);
        var calls = new List<(string Server, uint Mark)>();
        using var routes = new DnsRoutes(() => plan, Settings, (servers, mark) => new Book(servers[0], () => calls.Add((servers[0], mark))));
        var global = new Book("192.0.2.1");
        var resolver = new DnsResolver(global, new DnsCache(16), new DnsSets(new Ledger()), () => plan,
            Settings, new DnsState(), _ => Task.CompletedTask, routes: routes);
        var question = DnsBuilder.Question(1, "example.test");

        var first = await resolver.AnswerAsync(question, false, CancellationToken.None).ConfigureAwait(false);
        await resolver.AnswerAsync(question, false, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal("9.9.9.9", Address(first));
        Assert.Single(calls);

        plan = Plan([Rule() with { Outbound = "second" }]);
        Assert.Equal("8.8.8.8", Address(await resolver.AnswerAsync(question, false, CancellationToken.None).ConfigureAwait(false)));
        plan = Plan([Rule() with { Outbound = "second" }], [First, Second with { Dns = ["4.4.4.4"] }]);
        Assert.Equal("4.4.4.4", Address(await resolver.AnswerAsync(question, false, CancellationToken.None).ConfigureAwait(false)));
        Assert.Equal(3, calls.Count);
        Assert.Equal(0, global.Calls);

        plan = plan with { Ways = plan.Ways with { Alive = new HashSet<string>() } };
        Assert.Equal(DnsMessage.ServerFailure, DnsMessage.Read((await resolver.AnswerAsync(question, false, CancellationToken.None).ConfigureAwait(false))!)!.Code);
        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public async Task TheResolverScopesStandInsByTheEffectiveDnsPolicy()
    {
        var plan = Plan([Rule()]);
        var standIns = new DnsStandIns();
        using var routes = new DnsRoutes(() => plan, Settings, (servers, _) => new Book(servers[0]));
        var names = new DomainMatcher([new GeoDomain(GeoDomainKind.Domain, "example.test")]);
        var resolver = new DnsResolver(new Book("192.0.2.1"), new DnsCache(16), new DnsSets(new Ledger()), () => plan,
            Settings, new DnsState(), _ => Task.CompletedTask, standIns: standIns, standing: _ => names, routes: routes);
        var question = DnsBuilder.Question(1, "example.test");
        var first = IPAddress.Parse(Address(await resolver.AnswerAsync(question, false, IPAddress.Parse("10.8.0.2"), CancellationToken.None).ConfigureAwait(false)));
        plan = Plan([Rule() with { Outbound = "second" }]);
        var second = IPAddress.Parse(Address(await resolver.AnswerAsync(question, false, IPAddress.Parse("10.8.0.3"), CancellationToken.None).ConfigureAwait(false)));

        Assert.NotEqual(first, second);
        Assert.Equal("9.9.9.9", standIns.Real(first)!.ToString());
        Assert.Equal("8.8.8.8", standIns.Real(second)!.ToString());
    }

    [Fact]
    public async Task WarmingUsesThePolicyOfEachRule()
    {
        var plan = Plan([Rule(), Rule() with { Id = 2, Position = 2, Outbound = "second" }]);
        var calls = new List<string>();
        using var routes = new DnsRoutes(() => plan, Settings, (servers, _) => new Book(servers[0], () => calls.Add(servers[0])));
        var global = new Book("192.0.2.1");

        await DnsWarm.AskAsync(global, [new DnsWarmName(1, "example.test"), new DnsWarmName(2, "example.test")],
            CancellationToken.None, routes.ForRule).ConfigureAwait(false);

        Assert.Contains("9.9.9.9", calls);
        Assert.Contains("8.8.8.8", calls);
        Assert.Equal(0, global.Calls);
    }

    [Fact]
    public async Task BusyPoolsRemainAliveDuringEvictionAndDisposal()
    {
        var books = new List<WaitingBook>();
        var routes = new DnsRoutes(() => null, Settings, (_, _) =>
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

    private static RouteRule Rule() => new() { Id = 1, Name = "rule", Position = 1, Outbound = "first", Targets = ["example.test"] };

    private static RoutePlan Plan(IReadOnlyList<RouteRule> rules, IReadOnlyList<OutboundConfig>? outbounds = null,
        IReadOnlyList<Balancer>? balancers = null, IReadOnlyList<AddressGroup>? groups = null,
        IReadOnlyList<TunnelClient>? clients = null, IReadOnlySet<string>? alive = null) =>
        RoutePlan.Build(rules, outbounds ?? [First, Second], GeoIndex.Load([], new MemoryGeoFiles()), ["awg1", "awg2"],
            Settings, balancers, alive, clients, groups: groups);

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
