using System.Net;
using System.Reflection;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class DnsRouteTesterTests
{
    [Theory]
    [InlineData(DnsSelection.Routing)]
    [InlineData(DnsSelection.Outbound)]
    public async Task TheTesterResolvesThroughTheSelectedPolicyWithItsClientContext(string selection)
    {
        using var held = await Harness.CreateAsync(selection).ConfigureAwait(false);

        var result = await held.Tester.TestAsync(new RouteTestRequest("example.test", 443, "tcp", "client", 12345),
            CancellationToken.None).ConfigureAwait(false);

        Assert.Null(result.Fault);
        Assert.NotNull(result.Answer);
        Assert.Equal(["9.9.9.9"], result.Answer.Addresses);
        Assert.Equal("awg1", result.Answer.Inbound);
        Assert.Equal(["10.8.0.2"], result.Answer.Sources);
        Assert.Equal(2, held.Calls.Count);
        Assert.All(held.Calls, call => Assert.Equal(("9.9.9.9", 0xA601u), call));
    }

    [Fact]
    public async Task ASelectedPolicyFailureReturnsNoAddressesInsteadOfRequestingSystemResolution()
    {
        using var held = await Harness.CreateAsync(DnsSelection.Outbound).ConfigureAwait(false);
        held.Plans.Keep(held.Plans.Held! with
        {
            Ways = held.Plans.Held!.Ways with { Alive = new HashSet<string>() },
        });

        var answer = await held.Host.AskAsync("example.test", CancellationToken.None,
            IPAddress.Parse("10.8.0.2"), "awg1").ConfigureAwait(false);

        Assert.NotNull(answer);
        Assert.Empty(answer);
        Assert.Empty(held.Calls);
    }

    [Theory]
    [InlineData(DnsSelection.Routing)]
    [InlineData(DnsSelection.Outbound)]
    public async Task AStoppedPolicyResolverDoesNotRequestSystemResolution(string selection)
    {
        using var held = await Harness.CreateAsync(selection, false).ConfigureAwait(false);

        var answer = await held.Host.AskAsync("example.test", CancellationToken.None).ConfigureAwait(false);

        Assert.NotNull(answer);
        Assert.Empty(answer);
        Assert.Empty(held.Calls);
    }

    [Fact]
    public async Task AStoppedGlobalResolverKeepsRequestingSystemResolution()
    {
        using var held = await Harness.CreateAsync(DnsSelection.Global, false).ConfigureAwait(false);

        Assert.Null(await held.Host.AskAsync("example.test", CancellationToken.None).ConfigureAwait(false));
    }

    private sealed class Harness : IDisposable
    {
        private readonly Bench _bench;

        private readonly DnsRoutes _routes;

        private readonly DnsUpstream _global = new([], TimeSpan.Zero);

        /// <summary>
        /// ctor
        /// </summary>
        private Harness(Bench bench, DnsSettings settings, RoutePlan plan, bool running)
        {
            _bench = bench;
            Plans.Keep(plan);
            _routes = new DnsRoutes(() => Plans.Held, settings, (servers, mark) =>
                new Answer(servers[0], () => Calls.Add((servers[0], mark))));
            var sets = new DnsSets(new Ledger());
            var state = new DnsState();
            var book = new StandInBook(bench.Clock);
            var live = new BalanceLive();
            Host = new DnsHost(bench.Scopes, Plans, sets, state, new AccessNames(), book,
                new DnsHolders(), new DnsStandIns(), bench.Clock, NullLogger<DnsHost>.Instance);
            Keep(Host, "_settings", settings);
            if (running)
            {
                Keep(Host, "_upstream", _global);
                if (settings.Selection != DnsSelection.Global)
                {
                    Keep(Host, "_routes", _routes);
                }
            }

            var applier = new RouteApplier(bench.Rules, bench.Outbounds, bench.Balancers, bench.Configs,
                bench.Geo, bench.Resolver, state, bench.GeoFiles, Plans, live, sets, bench.Clients,
                null!, null!, null!, bench.Templates, book, new Ledger(), NullLogger<RouteApplier>.Instance);
            Tester = new RouteTester(applier, bench.Rules, bench.Clients, bench.Configs, bench.Outbounds,
                bench.Balancers, live, sets, Host);
        }

        public DnsHost Host { get; }

        public RouteTester Tester { get; }

        public RoutePlans Plans { get; } = new();

        public List<(string Server, uint Mark)> Calls { get; } = [];

        public static async Task<Harness> CreateAsync(string selection, bool running = true)
        {
            var bench = new Bench();
            var config = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with
            {
                Address = ["10.8.0.1/24"],
            }, CancellationToken.None).ConfigureAwait(false);
            Assert.True(config.IsOk, config.Message);
            var client = await bench.Clients.AddAsync(ClientDefaults.Fresh(config.Record!.Id, "client") with
            {
                Address = ["10.8.0.2/32"],
            }, CancellationToken.None).ConfigureAwait(false);
            Assert.True(client.IsOk, client.Message);
            var settings = DnsDefaults.Settings with
            {
                Selection = selection,
                Outbound = "first",
                Upstreams = ["1.1.1.1"],
            };
            var plan = RoutePlan.Build(
            [
                new RouteRule { Id = 1, Name = "future", Position = 1, Outbound = "second", Targets = ["example.test"], Ports = ["443"], SourcePorts = ["12345"], Protocol = "tcp" },
                new RouteRule { Id = 2, Name = "client", Position = 2, Outbound = "first", Targets = ["example.test"], Clients = ["client"], Inbounds = ["awg1"] },
                new RouteRule { Id = 3, Name = "fallback", Position = 3, Outbound = "second", Targets = ["example.test"] },
            ],
            [
                new OutboundConfig { Name = "first", Mark = 0xA601, Dns = ["9.9.9.9"] },
                new OutboundConfig { Name = "second", Mark = 0xA602, Dns = ["8.8.8.8"] },
            ], GeoIndex.Load([], new MemoryGeoFiles()), ["awg1"], settings, clients: [client.Record!]);

            return new Harness(bench, settings, plan, running);
        }

        public void Dispose()
        {
            Host.Dispose();
            _routes.Dispose();
            _global.Dispose();
            _bench.Dispose();
        }

        private static void Keep<T>(DnsHost host, string field, T value) =>
            typeof(DnsHost).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, value);
    }

    private sealed class Answer(string address, Action called) : IDnsUpstream
    {
        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            called();
            var asked = DnsMessage.Read(question.Span)!;
            var answers = asked.Type == DnsRecordType.A
                ? new[] { new Told(asked.Question, asked.Type, 300, address) }
                : [];

            return Task.FromResult<byte[]?>(DnsBuilder.Answer(asked.Id, asked.Question, asked.Type, answers));
        }
    }
}
