using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Status;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Proxy;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public class ServiceWatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly ServerConfig Awg1 = ConfigDefaults.Fresh("awg1") with { Id = 1, Host = "vpn.example.org", WebSocket = true, ServicesPort = 8446 };

    private static readonly ServerConfig Awg2 = ConfigDefaults.Fresh("awg2") with { Id = 2, Host = "vpn.example.org", ListenPort = 51821, ServicesPort = 8443 };

    [Fact]
    public void AHostThatRunsEverythingHasEveryServiceWorking()
    {
        var services = ServiceChecks.Of(Fine(Awg1, Awg2), Now);

        Assert.Equal(["subscription", "endpoint", "endpoint", "dns"], services.Select(one => one.Kind).ToArray());
        Assert.All(services, one => Assert.True(one.Works));
        Assert.Equal(["hello", "speed", "websocket", "subscription"], services[1].Parts);
        Assert.Equal([new ServicePort("udp", 51820), new ServicePort("tcp", 8446)], services[1].Ports);
        Assert.False(services[1].Shared);
        Assert.True(services[2].Shared);
        Assert.Empty(services[0].Ports);
        Assert.Equal("all 4 work", ServiceChecks.Line(services));
    }

    [Fact]
    public void AFrontThatFellSinceTheCheckBeforeIsDownWithWhy()
    {
        var facts = Fine(Awg1) with { Fronts = Front(new FrontFacts(true, "wstunnel ended with code 1", 2, true)) };

        var endpoint = ServiceChecks.Of(facts, Now)[1];

        Assert.False(endpoint.Works);
        Assert.Equal([new ServiceFault(ServiceChecks.FrontFell, "wstunnel ended with code 1")], endpoint.Faults);
    }

    [Fact]
    public void AFrontThatDoesNotRunOrDoesNotListenIsDown()
    {
        var down = ServiceChecks.Of(Fine(Awg1) with { Fronts = Front(new FrontFacts(false, "activating (auto-restart)", 0, false)) }, Now)[1];
        var deaf = ServiceChecks.Of(Fine(Awg1) with { Fronts = Front(new FrontFacts(true, string.Empty, 0, false)) }, Now)[1];
        var missing = ServiceChecks.Of(Fine(Awg1) with { Fronts = new Dictionary<string, FrontFacts>() }, Now)[1];

        Assert.Equal([new ServiceFault(ServiceChecks.FrontDown, "activating (auto-restart)")], down.Faults);
        Assert.Equal([new ServiceFault(ServiceChecks.FrontDeaf, "127.0.0.1:61001")], deaf.Faults);
        Assert.Equal([new ServiceFault(ServiceChecks.FrontDown, string.Empty)], missing.Faults);
    }

    [Fact]
    public void AnInterfaceTheHostLacksIsDownAndNamesAMissingModule()
    {
        var lacking = ServiceChecks.Of(Fine(Awg1) with { Links = new HashSet<string>() }, Now)[1];
        var bare = ServiceChecks.Of(Fine(Awg1) with { Links = new HashSet<string>(), Module = false }, Now)[1];
        var unread = ServiceChecks.Of(Fine(Awg1) with { Links = null, Module = false }, Now)[1];

        Assert.Equal([new ServiceFault(ServiceChecks.InterfaceDown, string.Empty)], lacking.Faults);
        Assert.Equal([new ServiceFault(ServiceChecks.InterfaceDown, ServiceChecks.NoModule)], bare.Faults);
        Assert.True(unread.Works);
    }

    [Fact]
    public void ThePortTheHostRefusedIsNamedAndLeavesTheSubscriptionsWithoutAnEndpoint()
    {
        var facts = Fine(Awg1) with { Refused = new Dictionary<int, string> { [8446] = "Address already in use" } };

        var services = ServiceChecks.Of(facts, Now);

        Assert.Equal([new ServiceFault(ServiceChecks.NoEndpoint, string.Empty)], services[0].Faults);
        Assert.Equal([new ServiceFault(ServiceChecks.PortRefused, "Address already in use")], services[1].Faults);
    }

    [Fact]
    public void ThePortOfThePanelIsNeverRefused()
    {
        var facts = Fine(Awg2) with { Refused = new Dictionary<int, string> { [8443] = "Address already in use" } };

        Assert.All(ServiceChecks.Of(facts, Now), one => Assert.True(one.Works));
    }

    [Fact]
    public void TheSubscriptionsOnTheirOwnPortCarryTheRefusalAndTheFirewall()
    {
        var own = SubscriptionDefaults.Settings with { Separate = true, Port = 8447 };
        var facts = Fine(Awg1) with
        {
            Subscription = own,
            SubscriptionFault = "subscription-port-busy",
            Walls = new Dictionary<ServicePort, string> { [new ServicePort("tcp", 8447)] = PortState.Closed },
        };

        var subscription = ServiceChecks.Of(facts, Now)[0];
        var inside = ServiceChecks.Of(facts with { Subscription = own with { Listen = ["127.0.0.1"] } }, Now)[0];
        var panel = ServiceChecks.Of(facts with { Subscription = own with { Port = 8443 }, Walls = new Dictionary<ServicePort, string>() }, Now)[0];

        Assert.Equal(
            [
                new ServiceFault(ServiceChecks.PortRefused, "subscription-port-busy"),
                new ServiceFault(ServiceChecks.PortClosed, "tcp 8447"),
            ],
            subscription.Faults);
        Assert.Equal([new ServicePort("tcp", 8447)], subscription.Ports);
        Assert.Equal([new ServiceFault(ServiceChecks.PortRefused, "subscription-port-busy")], inside.Faults);
        Assert.True(panel.Works);
        Assert.True(panel.Shared);
        Assert.DoesNotContain("subscription", ServiceChecks.Of(facts, Now)[1].Parts);
    }

    [Fact]
    public void APortTheFirewallKeepsClosedIsNamed()
    {
        var facts = Fine(Awg1) with
        {
            Walls = new Dictionary<ServicePort, string>
            {
                [new ServicePort("udp", 51820)] = PortState.Closed,
                [new ServicePort("tcp", 8446)] = PortState.Open,
            },
        };

        Assert.Equal([new ServiceFault(ServiceChecks.PortClosed, "udp 51820")], ServiceChecks.Of(facts, Now)[1].Faults);
    }

    [Fact]
    public void AnEndpointWithoutAHostIsDownForItsClientsGetNoAddress()
    {
        var bare = ServiceChecks.Of(Fine(Awg1 with { Host = " " }), Now)[1];

        Assert.Equal([new ServiceFault(ServiceChecks.NoHost, string.Empty)], bare.Faults);
        Assert.Equal("1 of 3 down: endpoint awg1 (no-host)", ServiceChecks.Line(ServiceChecks.Of(Fine(Awg1 with { Host = string.Empty }), Now)));
    }

    [Fact]
    public void TheLineOfTheMenuNamesTheClosedPorts()
    {
        var facts = Fine(Awg1) with
        {
            Walls = new Dictionary<ServicePort, string>
            {
                [new ServicePort("udp", 51820)] = PortState.Closed,
                [new ServicePort("tcp", 8446)] = PortState.Closed,
            },
        };

        var services = ServiceChecks.Of(facts, Now);

        Assert.Equal("1 of 3 down: endpoint awg1 (port-closed udp 51820, port-closed tcp 8446)", ServiceChecks.Line(services));
        Assert.Equal("port-closed (udp 51820); port-closed (tcp 8446)", ServiceChecks.Reason(services[1]));
    }

    [Fact]
    public void TheResolverSaysWhyItDoesNotRunOrWhereItsWayBroke()
    {
        var down = ServiceChecks.Of(Fine() with { Resolver = new ResolverFacts(true, false, 53, "there is no address", string.Empty) }, Now);
        var way = ServiceChecks.Of(Fine() with { Resolver = new ResolverFacts(true, true, 53, string.Empty, "the outbound is down") }, Now);

        Assert.Equal([new ServiceFault(ServiceChecks.ResolverDown, "there is no address")], down[^1].Faults);
        Assert.Equal([new ServiceFault(ServiceChecks.ResolverWay, "the outbound is down")], way[^1].Faults);
        Assert.Equal([new ServicePort("udp", 53)], way[^1].Ports);
    }

    [Fact]
    public void WhatIsTurnedOffIsLeftOut()
    {
        var facts = Fine(Awg1 with { IsEnabled = false }) with
        {
            Subscription = SubscriptionDefaults.Settings with { IsEnabled = false },
            Resolver = new ResolverFacts(false, false, 53, string.Empty, string.Empty),
        };

        Assert.Empty(ServiceChecks.Of(facts, Now));
        Assert.Equal("none to run", ServiceChecks.Line([]));
    }

    [Fact]
    public void TheFirewallIsAskedAboutEachPortOnceAndNotAboutALoopbackSubscription()
    {
        var shared = Awg2 with { ServicesPort = 8446 };
        var own = SubscriptionDefaults.Settings with { Separate = true, Port = 8447 };

        var ports = ServiceChecks.Exposed([Awg1, shared, Awg1 with { Id = 3, Name = "awg3", IsEnabled = false }], own);
        var inside = ServiceChecks.Exposed([Awg1], own with { Listen = ["127.0.0.1"] });

        Assert.Equal(
            [new ServicePort("udp", 51820), new ServicePort("tcp", 8446), new ServicePort("udp", 51821), new ServicePort("tcp", 8447)],
            ports);
        Assert.Equal([new ServicePort("udp", 51820), new ServicePort("tcp", 8446)], inside);
    }

    [Fact]
    public void TheLineOfTheMenuNamesWhatIsDown()
    {
        var facts = Fine(Awg1, Awg2) with
        {
            Fronts = Front(new FrontFacts(true, "wstunnel ended with code 1", 1, true)),
            Resolver = new ResolverFacts(true, false, 53, "port 53 is taken", string.Empty),
        };

        var services = ServiceChecks.Of(facts, Now);

        Assert.Equal("2 of 4 down: endpoint awg1 (front-fell), dns (resolver-down)", ServiceChecks.Line(services));
        Assert.Equal("front-fell (wstunnel ended with code 1)", ServiceChecks.Reason(services[1]));
        Assert.Equal("endpoint awg1", ServiceChecks.Label(services[1]));
        Assert.Equal("subscriptions", ServiceChecks.Label(services[0]));
    }

    [Fact]
    public void SystemdTellsWhetherTheFrontRunsAndHowOftenItFell()
    {
        var running = SystemdProxies.StateOf("ActiveState=active\nSubState=running\nNRestarts=3\nExecMainStatus=0\n");
        var restarting = SystemdProxies.StateOf("NRestarts=5\nExecMainStatus=1\nActiveState=activating\nSubState=auto-restart\n");
        var gone = SystemdProxies.StateOf("ActiveState=inactive\nSubState=dead\nNRestarts=0\nExecMainStatus=0\n");

        Assert.Equal(new ProxyState(true, string.Empty, 3), running);
        Assert.Equal(new ProxyState(false, "activating (auto-restart): wstunnel ended with code 1", 5), restarting);
        Assert.Equal(new ProxyState(false, "inactive (dead)", 0), gone);
    }

    [Fact]
    public async Task SystemdIsAskedForTheStateOfTheServiceOfTheFront()
    {
        var tools = new Tools();
        tools.Answers["systemctl show amneziageo-proxy@awg1"] = new CommandResult(0, "ActiveState=active\nNRestarts=2\n", string.Empty);

        var state = await new SystemdProxies(tools).StateAsync("awg1", CancellationToken.None);

        Assert.Equal(new ProxyState(true, string.Empty, 2), state);
        Assert.Equal(["systemctl show amneziageo-proxy@awg1 --property=ActiveState,SubState,NRestarts,ExecMainStatus"], tools.Calls);
    }

    [Fact]
    public async Task TheFirewallIsReadOnceForEveryPort()
    {
        var tools = new Tools();
        tools.Answers["ufw status verbose"] = new CommandResult(
            0,
            "Status: active\nDefault: deny (incoming), allow (outgoing), deny (routed)\n\nTo    Action    From\n--    ------    ----\n51820/udp    ALLOW IN    Anywhere\n",
            string.Empty);
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var states = await host.StatesAsync(
            [new FirewallPort("udp", 51820, string.Empty), new FirewallPort("tcp", 8446, string.Empty)],
            CancellationToken.None);

        Assert.Equal([PortState.Open, PortState.Closed], states);
        Assert.Equal(["ufw status verbose"], tools.Calls);
    }

    [Fact]
    public async Task TheWatchSaysWhenAFrontFallsOverAndWhenItWorksAgain()
    {
        using var bench = new Bench(now: Now);
        using var place = new Place();
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("lo") with { Host = "vpn.example.org", Address = ["10.8.0.1/24"], WebSocket = true, ServicesPort = Free() },
            CancellationToken.None)).Record!;
        using var front = new TcpListener(IPAddress.Loopback, ConfigServices.Front(endpoint));
        front.Start();
        var runner = new Runner();
        var fronts = new ProxyHost(new Ledger(), runner, place.Root);
        var tools = new Tools();
        tools.Answers["ufw status verbose"] = new CommandResult(0, "Status: inactive\n", string.Empty);
        var heard = new Heard();
        var server = new ServiceServer(
            bench.Scopes,
            new ServiceDesk(
                bench.Scopes,
                new SpeedTickets(bench.Clock),
                [],
                new SubscriptionState(),
                new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance),
                Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Http.Json.JsonOptions()),
                NullLogger<ServiceDesk>.Instance),
            fronts,
            new WebOptions(),
            PanelDefaults.Settings,
            new ServiceShare(),
            NullLogger<ServiceServer>.Instance);
        using var watch = new ServiceWatch(
            bench.Scopes,
            server,
            fronts,
            new SubscriptionState(),
            new FirewallHost(tools, new Ledger(), "ufw"),
            new DnsState(),
            PanelDefaults.Settings,
            new WebOptions(),
            new PanelHealth("http://127.0.0.1:8443/api/health", Path.Combine(place.Root, "health")),
            bench.Clock,
            heard);
        try
        {
            var first = await watch.CheckAsync(CancellationToken.None);
            runner.State = new ProxyState(true, "wstunnel ended with code 1", 2);
            bench.Clock.Pass(TimeSpan.FromSeconds(30));
            var fallen = await watch.CheckAsync(CancellationToken.None);
            var written = await File.ReadAllTextAsync(Path.Combine(place.Root, ServiceWatch.FileName));
            bench.Clock.Pass(TimeSpan.FromSeconds(30));
            var again = await watch.CheckAsync(CancellationToken.None);

            Assert.All(first.Services, one => Assert.True(one.Works));
            Assert.Equal(Now, first.Checked);
            Assert.Equal([new ServiceFault(ServiceChecks.FrontFell, "wstunnel ended with code 1")], fallen.Services[1].Faults);
            Assert.Equal(Now.AddSeconds(30), fallen.Services[1].Since);
            Assert.Equal(Now, fallen.Services[0].Since);
            Assert.Equal("1 of 2 down: endpoint lo (front-fell)\n", written);
            Assert.True(again.Services[1].Works);
            Assert.Equal(Now.AddSeconds(60), again.Services[1].Since);
            Assert.Same(again, watch.Report);
            Assert.Equal(
                [
                    "Warning the service endpoint lo is down: front-fell (wstunnel ended with code 1)",
                    "Information the service endpoint lo works again",
                ],
                heard.Lines);
            Assert.Equal(["ufw status verbose"], tools.Calls);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public void ACertificateThatDoesNotReadOrRanOutIsDown()
    {
        var facts = Fine(Awg1) with
        {
            Certificates =
            [
                new CertificateFacts("vpn.example.org", null, "Could not find file"),
                new CertificateFacts("sub.example.org", Now.AddDays(-2), string.Empty),
            ],
        };

        var services = ServiceChecks.Of(facts, Now);

        Assert.Equal(["subscription", "endpoint", "dns", "certificate", "certificate"], services.Select(one => one.Kind).ToArray());
        Assert.Equal([new ServiceFault(ServiceChecks.CertificateUnreadable, "Could not find file")], services[3].Faults);
        Assert.Equal([new ServiceFault(ServiceChecks.CertificateExpired, "2026-09-26")], services[4].Faults);
        Assert.Null(services[3].Until);
        Assert.Equal(Now.AddDays(-2), services[4].Until);
        Assert.Equal("certificate sub.example.org", ServiceChecks.Label(services[4]));
        Assert.Equal(
            "2 of 5 down: certificate vpn.example.org (certificate-unreadable), certificate sub.example.org (certificate-expired)",
            ServiceChecks.Line(services));
    }

    [Fact]
    public void ACertificateThatRunsOutWithinTwoWeeksWorksAndSaysInHowManyDays()
    {
        var soon = ServiceChecks.Of(Fine() with { Certificates = [new CertificateFacts("vpn.example.org", Now.AddDays(8).AddHours(3), string.Empty)] }, Now)[^1];
        var today = ServiceChecks.Of(Fine() with { Certificates = [new CertificateFacts("vpn.example.org", Now.AddHours(5), string.Empty)] }, Now)[^1];
        var edge = ServiceChecks.Of(Fine() with { Certificates = [new CertificateFacts("vpn.example.org", Now.AddDays(14), string.Empty)] }, Now)[^1];
        var far = ServiceChecks.Of(Fine() with { Certificates = [new CertificateFacts("vpn.example.org", Now.AddDays(60), string.Empty)] }, Now)[^1];

        Assert.True(soon.Works);
        Assert.Equal([new ServiceFault(ServiceChecks.CertificateExpiring, "8")], soon.Notes);
        Assert.Equal([new ServiceFault(ServiceChecks.CertificateExpiring, "0")], today.Notes);
        Assert.Empty(edge.Notes);
        Assert.Empty(far.Notes);
        Assert.Equal(Now.AddDays(60), far.Until);
        Assert.Equal("all 4 work", ServiceChecks.Line(ServiceChecks.Of(Fine(Awg1) with { Certificates = [new CertificateFacts("vpn.example.org", Now.AddDays(8), string.Empty)] }, Now)));
    }

    [Fact]
    public void TheFileOfACertificateTellsItsNameAndItsEnd()
    {
        using var place = new Place();
        var until = new DateTimeOffset(2027, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var chain = Certificate(place.Root, "vpn.example.org", until);
        var broken = Path.Combine(place.Root, "vpn.example.org", "broken.pem");
        File.WriteAllText(broken, "not a certificate");

        var read = ServiceCertificates.Read(chain, NullLogger.Instance);
        var unread = ServiceCertificates.Read(broken, NullLogger.Instance);
        var missing = ServiceCertificates.Read(Path.Combine(place.Root, "gone", "fullchain.pem"), NullLogger.Instance);

        Assert.Equal(new CertificateFacts("vpn.example.org", until, string.Empty), read);
        Assert.Equal("vpn.example.org", unread.Name);
        Assert.Null(unread.Until);
        Assert.NotEmpty(unread.Fault);
        Assert.Equal("gone", missing.Name);
        Assert.Null(missing.Until);
        Assert.NotEmpty(missing.Fault);
    }

    [Fact]
    public async Task TheWatchSaysOnceThatACertificateRunsOutSoon()
    {
        using var bench = new Bench(now: Now);
        using var place = new Place();
        var options = new WebOptions { Certificate = Certificate(place.Root, "vpn.example.org", Now.AddDays(5).AddHours(1)) };
        var fronts = new ProxyHost(new Ledger(), new Runner(), place.Root);
        var tools = new Tools();
        tools.Answers["ufw status verbose"] = new CommandResult(0, "Status: inactive\n", string.Empty);
        var heard = new Heard();
        var server = new ServiceServer(
            bench.Scopes,
            new ServiceDesk(
                bench.Scopes,
                new SpeedTickets(bench.Clock),
                [],
                new SubscriptionState(),
                new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance),
                Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Http.Json.JsonOptions()),
                NullLogger<ServiceDesk>.Instance),
            fronts,
            options,
            PanelDefaults.Settings,
            new ServiceShare(),
            NullLogger<ServiceServer>.Instance);
        using var watch = new ServiceWatch(
            bench.Scopes,
            server,
            fronts,
            new SubscriptionState(),
            new FirewallHost(tools, new Ledger(), "ufw"),
            new DnsState(),
            PanelDefaults.Settings,
            options,
            new PanelHealth("http://127.0.0.1:8443/api/health", Path.Combine(place.Root, "health")),
            bench.Clock,
            heard);
        try
        {
            var first = await watch.CheckAsync(CancellationToken.None);
            bench.Clock.Pass(TimeSpan.FromSeconds(30));
            var second = await watch.CheckAsync(CancellationToken.None);

            var certificate = first.Services[^1];
            Assert.Equal(ServiceChecks.Certificate, certificate.Kind);
            Assert.Equal("vpn.example.org", certificate.Name);
            Assert.True(certificate.Works);
            Assert.Equal([new ServiceFault(ServiceChecks.CertificateExpiring, "5")], certificate.Notes);
            Assert.Equal(Now.AddDays(5).AddHours(1), certificate.Until);
            Assert.Equal(Now, second.Services[^1].Since);
            Assert.Single(heard.Lines, one => one == "Warning the certificate vpn.example.org runs out in 5 days");
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    private static ServiceFacts Fine(params ServerConfig[] configs) => new(
        configs,
        PanelDefaults.Port,
        SubscriptionDefaults.Settings,
        string.Empty,
        new Dictionary<int, string>(),
        configs.Where(one => one.WebSocket).ToDictionary(one => one.Name, _ => new FrontFacts(true, string.Empty, 0, true)),
        true,
        configs.Select(one => one.Name).ToHashSet(StringComparer.Ordinal),
        new Dictionary<ServicePort, string>(),
        new ResolverFacts(true, true, 53, string.Empty, string.Empty),
        []);

    // Writes a certificate made up for a name that runs out at the moment given and returns its file.
    private static string Certificate(string root, string name, DateTimeOffset until)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        var chain = Path.Combine(folder, "fullchain.pem");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(name);
        request.CertificateExtensions.Add(names.Build());
        using var made = request.CreateSelfSigned(until.AddDays(-90), until);
        File.WriteAllText(chain, made.ExportCertificatePem());

        return chain;
    }

    private static Dictionary<string, FrontFacts> Front(FrontFacts front) => new() { ["awg1"] = front };

    private static int Free()
    {
        using var probe = new TcpListener(IPAddress.IPv6Any, 0);
        probe.Server.DualMode = true;
        probe.Start();

        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    // A front that says what the test tells it to.
    private sealed class Runner : IProxyRunner
    {
        public ProxyState State { get; set; } = ProxyState.Up;

        public Task<ProxyState> EnableAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Down);

        public Task<ProxyState> StartAsync(string name, CancellationToken ct) => Task.FromResult(State);

        public Task<ProxyState> StopAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Down);

        public Task<ProxyState> StateAsync(string name, CancellationToken ct) => Task.FromResult(State);
    }

    // Keeps what the watch writes to its log, one line a record.
    private sealed class Heard : ILogger<ServiceWatch>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{logLevel} {formatter(state, exception)}");
    }

    // A directory of its own for the files of the fronts and of the panel.
    private sealed class Place : IDisposable
    {
        public Place()
        {
            Root = Path.Combine(Path.GetTempPath(), $"amneziageo-watch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            Directory.Delete(Root, true);
        }
    }
}
