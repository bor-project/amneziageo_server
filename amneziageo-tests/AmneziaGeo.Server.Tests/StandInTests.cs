using System.Globalization;
using System.Net;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;
using AmneziaGeo.Server.Routing.Template;
using Microsoft.Extensions.DependencyInjection;

namespace AmneziaGeo.Server.Tests;

public class StandInTests
{
    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "direct", Kind = OutboundKind.Local, Mark = 0xA601, Table = OutboundRules.MainTable },
    ];

    private static readonly DnsSettings On = DnsDefaults.Settings with { IsEnabled = true };

    private static readonly IPAddress Asking = IPAddress.Parse("10.8.0.2");

    private static readonly IPNetwork Range = IPNetwork.Parse(TemplateDefaults.StandIns);

    [Fact]
    public void ANameKeepsItsStandInAddress()
    {
        var standIns = new DnsStandIns();

        var first = standIns.Take("rr1.googlevideo.com", [IPAddress.Parse("1.2.3.4")], out _);
        var again = standIns.Take("RR1.googlevideo.com.", [IPAddress.Parse("1.2.3.4")], out _);

        Assert.Equal(first, again);
        Assert.True(Range.Contains(first));
        Assert.Equal(1, standIns.Count);
    }

    [Fact]
    public void ANameLandsOnTheSameAddressOnAnotherServer()
    {
        var one = new DnsStandIns().Take("media.frix7-1.fna.whatsapp.net", [IPAddress.Parse("87.245.208.98")], out _);
        var other = new DnsStandIns().Take("media.frix7-1.fna.whatsapp.net", [IPAddress.Parse("31.13.72.52")], out _);

        Assert.Equal(one, other);
    }

    [Fact]
    public void EveryNameTakesAnAddressOfItsOwn()
    {
        var standIns = new DnsStandIns();

        var taken = Enumerable.Range(0, 2000)
            .Select(at => standIns.Take($"rr{at}.googlevideo.com", [IPAddress.Parse("1.2.3.4")], out _))
            .ToArray();

        Assert.Equal(2000, taken.Distinct().Count());
        Assert.All(taken, address => Assert.True(Range.Contains(address)));
    }

    [Fact]
    public void AStandInLeadsToTheRealAddressItWasAnsweredWith()
    {
        var standIns = new DnsStandIns();

        var address = standIns.Take("mmg.whatsapp.net", [IPAddress.Parse("157.240.205.60"), IPAddress.Parse("2a03:2880::1")], out _);

        Assert.Equal(IPAddress.Parse("157.240.205.60"), standIns.Real(address));
        Assert.Null(standIns.Real(IPAddress.Parse("198.19.255.254")));
    }

    [Fact]
    public void ARealAddressStillAnsweredStaysInPlace()
    {
        var standIns = new DnsStandIns();
        var address = standIns.Take("mmg.whatsapp.net", [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("2.2.2.2")], out _);
        standIns.Changes();

        standIns.Take("mmg.whatsapp.net", [IPAddress.Parse("2.2.2.2"), IPAddress.Parse("1.1.1.1")], out var waiting);

        Assert.False(waiting);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), standIns.Real(address));
        Assert.Equal(string.Empty, standIns.Changes());
    }

    [Fact]
    public void TheHostLearnsANewStandInOnce()
    {
        var standIns = new DnsStandIns();

        var address = standIns.Take("www.youtube.com", [IPAddress.Parse("1.2.3.4")], out var first);
        var text = standIns.Changes();
        standIns.Take("www.youtube.com", [IPAddress.Parse("1.2.3.4")], out var second);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal($"add element inet amneziageo_rt stand {{ {address} : 1.2.3.4 }}\n", text);
        Assert.Equal(string.Empty, standIns.Changes());
    }

    [Fact]
    public void AStandInThatLeadsElsewhereIsLaidAnew()
    {
        var standIns = new DnsStandIns();
        var address = standIns.Take("www.youtube.com", [IPAddress.Parse("1.2.3.4")], out _);
        standIns.Changes();

        standIns.Take("www.youtube.com", [IPAddress.Parse("5.6.7.8")], out var waiting);
        var text = standIns.Changes();

        Assert.True(waiting);
        Assert.Equal(
            $"delete element inet amneziageo_rt stand {{ {address} }}\nadd element inet amneziageo_rt stand {{ {address} : 5.6.7.8 }}\n",
            text);
    }

    [Fact]
    public void AFreshMapTakesEveryStandIn()
    {
        var standIns = new DnsStandIns();
        standIns.Take("a.example", [IPAddress.Parse("1.1.1.1")], out _);
        standIns.Take("b.example", [IPAddress.Parse("2.2.2.2")], out _);

        var text = standIns.Lay();

        Assert.StartsWith("add element inet amneziageo_rt stand { ", text, StringComparison.Ordinal);
        Assert.Contains(" : 1.1.1.1", text, StringComparison.Ordinal);
        Assert.Contains(" : 2.2.2.2", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, standIns.Changes());
    }

    [Fact]
    public void AMapTheHostLostIsLaidWhole()
    {
        var standIns = new DnsStandIns();
        var address = standIns.Take("a.example", [IPAddress.Parse("1.1.1.1")], out _);
        standIns.Changes();

        standIns.Spoil();

        Assert.Equal(
            $"flush map inet amneziageo_rt stand\nadd element inet amneziageo_rt stand {{ {address} : 1.1.1.1 }}\n",
            standIns.Changes());
    }

    [Fact]
    public void ANameNobodyAsksAboutForAWeekLetsItsAddressGo()
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
        var standIns = new DnsStandIns(clock);
        var old = standIns.Take("old.example", [IPAddress.Parse("1.1.1.1")], out _);
        standIns.Changes();
        clock.Pass(TimeSpan.FromDays(6));
        standIns.Take("fresh.example", [IPAddress.Parse("2.2.2.2")], out _);
        standIns.Changes();

        clock.Pass(TimeSpan.FromDays(2));
        var gone = standIns.Sweep();

        Assert.Equal(1, gone);
        Assert.Equal(1, standIns.Count);
        Assert.Equal($"delete element inet amneziageo_rt stand {{ {old} }}\n", standIns.Changes());
    }

    [Fact]
    public void AFullRangeLetsTheNameAskedAboutLongestAgoGo()
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
        var standIns = new DnsStandIns(clock, "198.19.0.0/30");
        var first = standIns.Take("a.example", [IPAddress.Parse("1.1.1.1")], out _);
        clock.Pass(TimeSpan.FromMinutes(1));
        standIns.Take("b.example", [IPAddress.Parse("2.2.2.2")], out _);
        clock.Pass(TimeSpan.FromMinutes(1));

        var third = standIns.Take("c.example", [IPAddress.Parse("3.3.3.3")], out _);

        Assert.Equal(first, third);
        Assert.Equal(2, standIns.Count);
        Assert.Equal(IPAddress.Parse("3.3.3.3"), standIns.Real(third));
    }

    [Fact]
    public void TheStandInsComeBackAfterAStart()
    {
        var standIns = new DnsStandIns();
        var address = standIns.Take("www.youtube.com", [IPAddress.Parse("1.2.3.4")], out _);
        var other = new DnsStandIns();

        other.Restore([.. standIns.Held(), new DnsStandIn("outside.example", IPAddress.Parse("10.0.0.1"), null, DateTimeOffset.UnixEpoch)]);

        Assert.Equal(1, other.Count);
        Assert.Equal(IPAddress.Parse("1.2.3.4"), other.Real(address));
        Assert.Equal(address, other.Take("www.youtube.com", [IPAddress.Parse("1.2.3.4")], out _));
    }

    [Fact]
    public async Task AClientOfAStandInTemplateIsAnsweredWithAStandIn()
    {
        var (resolver, standIns, host) = Resolver(new Book(("rr1.googlevideo.com", "1.2.3.4")));

        var answer = DnsMessage.Read(await Ask(resolver, "rr1.googlevideo.com"))!;
        var address = Assert.Single(answer.Addresses);

        Assert.Equal(3, answer.Id);
        Assert.Equal(0, answer.Code);
        Assert.True(Range.Contains(address));
        Assert.Equal(IPAddress.Parse("1.2.3.4"), standIns.Real(address));
        Assert.InRange(answer.Lifetime, (uint)On.MinTtl, DnsDefaults.StandInSeconds);
        Assert.Equal($"add element inet amneziageo_rt stand {{ {address} : 1.2.3.4 }}\n", host.Ruleset);
    }

    [Fact]
    public async Task ANameOutsideTheTemplateIsAnsweredAsItIs()
    {
        var (resolver, standIns, _) = Resolver(new Book(("ya.ru", "5.255.255.242")));

        var answer = DnsMessage.Read(await Ask(resolver, "ya.ru"))!;

        Assert.Equal(IPAddress.Parse("5.255.255.242"), Assert.Single(answer.Addresses));
        Assert.Equal(0, standIns.Count);
    }

    [Fact]
    public async Task AClientOutsideTheTemplateIsAnsweredAsItIs()
    {
        var (resolver, standIns, _) = Resolver(new Book(("rr1.googlevideo.com", "1.2.3.4")));

        var answer = DnsMessage.Read(await Ask(resolver, "rr1.googlevideo.com", client: IPAddress.Parse("10.8.0.9")))!;

        Assert.Equal(IPAddress.Parse("1.2.3.4"), Assert.Single(answer.Addresses));
        Assert.Equal(0, standIns.Count);
    }

    [Theory]
    [InlineData(DnsRecordType.Aaaa)]
    [InlineData(DnsRecordType.Https)]
    public async Task AStandInNameGivesNoOtherAddresses(DnsRecordType type)
    {
        var (resolver, _, _) = Resolver(new Book(("www.youtube.com", "2a00:1450:4010:c05::5d")));

        var answer = DnsMessage.Read(await Ask(resolver, "www.youtube.com", type))!;

        Assert.Equal(0, answer.Code);
        Assert.True(answer.IsResponse);
        Assert.Equal(type, answer.Type);
        Assert.Empty(answer.Answers);
    }

    [Fact]
    public async Task ANameThatLeadsIntoTheTemplateThroughAnAliasTakesAStandIn()
    {
        var (resolver, standIns, _) = Resolver(new Alias("video.example.org", "rr5.googlevideo.com", "1.2.3.4"));

        var answer = DnsMessage.Read(await Ask(resolver, "video.example.org"))!;
        var address = Assert.Single(answer.Addresses);

        Assert.True(Range.Contains(address));
        Assert.Equal(IPAddress.Parse("1.2.3.4"), standIns.Real(address));
    }

    [Fact]
    public async Task ANameThatIsNotStaysUnanswered()
    {
        var (resolver, standIns, _) = Resolver(new Book());

        var answer = DnsMessage.Read(await Ask(resolver, "nowhere.youtube.com"))!;

        Assert.Equal(3, answer.Code);
        Assert.Equal(0, standIns.Count);
    }

    [Fact]
    public async Task TheRulesTakeTheRealAddressOfAStandInName()
    {
        var host = new Ledger();
        var standIns = new DnsStandIns();
        var sets = new DnsSets(host, null, DnsDefaults.StandingAddresses, standIns);
        var rule = RouteDefaults.Fresh("yt") with { Id = 1, Action = RouteAction.Out, Outbound = "direct", Targets = ["youtube.com"] };
        var plan = Plan([rule]);
        var resolver = new DnsResolver(
            new Book(("www.youtube.com", "1.2.3.4")),
            new DnsCache(16),
            sets,
            () => plan,
            On,
            new DnsState(),
            ct => sets.FlushAsync(plan, On.NameLifetime, ct),
            null,
            standIns,
            client => client.Equals(Asking) ? Names("youtube.com") : null);

        await Ask(resolver, "www.youtube.com");

        Assert.True(sets.Holds(1, IPAddress.Parse("1.2.3.4")));
    }

    [Fact]
    public void TheHostLeadsTheStandInsToTheRealAddressesAndDecidesThemByThose()
    {
        var text = RouteRuleset.Text(Plan([]));

        Assert.Contains("\tmap stand {\n\t\ttype ipv4_addr : ipv4_addr\n\t}\n", text, StringComparison.Ordinal);
        Assert.Contains("\t\tct state != new accept\n\t\tip daddr 198.19.0.0/16 accept\n\t\tjump decide\n", text, StringComparison.Ordinal);
        Assert.Contains("type nat hook prerouting priority dstnat; policy accept;\n\t\tiifname != { \"awg1\" } accept\n\t\tdnat ip to ip daddr map @stand\n", text, StringComparison.Ordinal);
        Assert.Contains(
            "type filter hook prerouting priority dstnat + 10; policy accept;\n\t\tiifname != { \"awg1\" } accept\n\t\tct state != new accept\n"
            + "\t\tmeta nfproto != ipv4 accept\n\t\tip daddr 198.19.0.0/16 accept\n\t\tct original ip daddr != 198.19.0.0/16 accept\n\t\tjump decide\n",
            text,
            StringComparison.Ordinal);
        Assert.Contains("ip daddr 198.19.0.0/16 meta l4proto tcp reject with tcp reset\n", text, StringComparison.Ordinal);
        Assert.Contains("ip daddr 198.19.0.0/16 reject\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutStandInsTheHostCarriesNoMap()
    {
        var text = RouteRuleset.Text(Plan([]) with { StandIns = false });

        Assert.DoesNotContain("stand", text, StringComparison.Ordinal);
        Assert.DoesNotContain("198.19.0.0/16", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStandInsGoToTheHostInTheSameStepAsTheRules()
    {
        var host = new Ledger();
        var standIns = new DnsStandIns();
        var sets = new DnsSets(host, null, DnsDefaults.StandingAddresses, standIns);
        var address = standIns.Take("www.youtube.com", [IPAddress.Parse("1.2.3.4")], out _);
        var plan = Plan([]);

        await sets.LayAsync(RouteRuleset.Text(plan), plan, CancellationToken.None);

        Assert.EndsWith($"}}\nadd element inet amneziageo_rt stand {{ {address} : 1.2.3.4 }}\n", host.Ruleset, StringComparison.Ordinal);
        Assert.Equal(string.Empty, standIns.Changes());
    }

    [Fact]
    public async Task AHostWithoutTheMapIsSentNoStandIn()
    {
        var host = new Ledger();
        var standIns = new DnsStandIns();
        var sets = new DnsSets(host, null, DnsDefaults.StandingAddresses, standIns);
        standIns.Take("www.youtube.com", [IPAddress.Parse("1.2.3.4")], out _);
        var plan = Plan([]) with { StandIns = false };

        await sets.LayAsync(RouteRuleset.Text(plan), plan, CancellationToken.None);
        await sets.FlushAsync(plan, On.NameLifetime, CancellationToken.None);

        Assert.DoesNotContain("stand", host.Ruleset, StringComparison.Ordinal);
    }

    [Fact]
    public void AStandInTemplateHandsTheResolverTheRangeAndItsNetworks()
    {
        var template = new ClientTemplate { Name = "blocked", StandIns = true, AllowedIps = ["91.108.4.0/22"], Dns = ["9.9.9.9"] };

        var text = ClientText.Text(Endpoint(), Client(), template, ["10.8.0.1"]);

        Assert.Contains("DNS = 10.8.0.1\n", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 91.108.4.0/22, 198.19.0.0/16, 10.8.0.1/32\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStandInTemplateOfNamesAloneCarriesOnlyTheRange()
    {
        var template = new ClientTemplate { Name = "names", StandIns = true };

        var text = ClientText.Text(Endpoint(), Client(), template, ["10.8.0.1"]);

        Assert.Contains("AllowedIPs = 198.19.0.0/16, 10.8.0.1/32\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStandInTemplateWithoutTheResolverIsWrittenAsBefore()
    {
        var template = new ClientTemplate { Name = "blocked", StandIns = true, AllowedIps = ["91.108.4.0/22"], Dns = ["9.9.9.9"] };

        var text = ClientText.Text(Endpoint(), Client(), template);

        Assert.Contains("DNS = 9.9.9.9\n", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 91.108.4.0/22\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNamesOfAStandInTemplateAreNotAskedAbout()
    {
        var upstream = new Book(("www.youtube.com", "1.2.3.4"));

        var found = await new TemplateResolver(upstream).ResolveAsync(
            ["geoip:ru", "geosite:yandex", "domain:youtube.com"],
            Index(),
            true,
            CancellationToken.None);

        Assert.Empty(upstream.Asked);
        Assert.Equal(["77.88.8.0/24"], found.AllowedIps);
        Assert.Empty(found.Missed);
    }

    [Fact]
    public void ATemplateStandsInForTheNamesOfItsSitesAndDomains()
    {
        var template = new ClientTemplate { Name = "t", Entries = ["geoip:ru", "geosite:yandex", "domain:kimi.com"] };

        var names = StandInBook.Matcher(template, Index());

        Assert.True(names.Matches("mail.yandex.ru"));
        Assert.True(names.Matches("ya.ru"));
        Assert.True(names.Matches("www.kimi.com"));
        Assert.False(names.Matches("google.com"));
    }

    [Fact]
    public async Task TheBookKnowsTheClientsOfTheStandInTemplates()
    {
        using var bench = new Bench();
        var endpoint = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] }, CancellationToken.None);
        var standing = await bench.Templates.AddAsync(
            new ClientTemplate { Name = "stood", StandIns = true, Entries = ["domain:youtube.com"] },
            CancellationToken.None);
        var plain = await bench.Templates.AddAsync(
            new ClientTemplate { Name = "plain", Entries = ["domain:youtube.com"] },
            CancellationToken.None);
        var anna = await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Record!.Id, "anna") with { Address = ["10.8.0.2/32"], TemplateId = standing.Record!.Id },
            CancellationToken.None);
        var boris = await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Record!.Id, "boris") with { Address = ["10.8.0.3/32"], TemplateId = plain.Record!.Id },
            CancellationToken.None);
        var book = new StandInBook(bench.Clock);
        Assert.True(anna.IsOk, anna.Message);
        Assert.True(boris.IsOk, boris.Message);

        using var scope = bench.Scopes.CreateScope();
        await book.RefreshAsync(scope.ServiceProvider, CancellationToken.None);

        Assert.True(book.For(IPAddress.Parse("10.8.0.2"))?.Matches("www.youtube.com"));
        Assert.Null(book.For(IPAddress.Parse("10.8.0.3")));
        Assert.False(book.Due);
    }

    [Fact]
    public async Task ATemplateKeepsItsStandInSwitch()
    {
        using var bench = new Bench();

        var added = await bench.Templates.AddAsync(new ClientTemplate { Name = "stood", StandIns = true }, CancellationToken.None);
        var read = await bench.Templates.FindAsync(added.Record!.Id, CancellationToken.None);

        Assert.True(read!.StandIns);
    }

    [Fact]
    public async Task TheStandInsAreSavedAndReadBack()
    {
        using var bench = new Bench();
        var store = new StandInStore(bench.Db);
        var seen = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(
            [
                new DnsStandIn("www.youtube.com", IPAddress.Parse("198.19.1.2"), IPAddress.Parse("1.2.3.4"), seen),
                new DnsStandIn("mmg.whatsapp.net", IPAddress.Parse("198.19.3.4"), null, seen),
            ],
            CancellationToken.None);
        await store.SaveAsync([new DnsStandIn("www.youtube.com", IPAddress.Parse("198.19.1.2"), IPAddress.Parse("5.6.7.8"), seen)], CancellationToken.None);
        var read = await store.ListAsync(CancellationToken.None);

        var one = Assert.Single(read);
        Assert.Equal("www.youtube.com", one.Name);
        Assert.Equal(IPAddress.Parse("198.19.1.2"), one.Address);
        Assert.Equal(IPAddress.Parse("5.6.7.8"), one.Real);
        Assert.Equal(seen, one.Seen);
    }

    [Fact]
    public async Task TheResolverNamesTheInterfacesTheFirewallKeepsItsClientsFrom()
    {
        var tools = new Tools();
        tools.Answers["nft list chain ip filter INPUT"] = new CommandResult(0, "chain INPUT {\n type filter hook input priority filter; policy drop;\n}\n", string.Empty);
        tools.Answers["nft list chain ip filter ufw-before-input"] = new CommandResult(0, string.Empty, string.Empty);
        tools.Answers["nft list chain ip filter ufw-user-input"] = new CommandResult(0, "iifname \"awg0\" meta l4proto udp udp dport 53 counter packets 0 bytes 0 accept\n", string.Empty);
        tools.Answers["nft list chain ip6"] = new CommandResult(1, string.Empty, "Error: No such file or directory");
        var clock = new Clock(DateTimeOffset.Parse("2026-10-18T18:00:00Z", CultureInfo.InvariantCulture));
        var doors = new ResolverDoors(new FirewallHost(tools, new Ledger(), string.Empty), clock);
        ServerConfig[] configs =
        [
            ConfigDefaults.Fresh("awg0") with { Id = 1, Address = ["10.0.0.1/24"] },
            ConfigDefaults.Fresh("awg1") with { Id = 2, Address = ["10.0.1.1/24"] },
            ConfigDefaults.Fresh("awg2") with { Id = 3, Address = ["10.0.2.1/24"], IsEnabled = false },
        ];
        var on = new DnsSettings { IsEnabled = true, Port = 53 };
        var off = new DnsSettings { IsEnabled = false, Port = 53 };

        Assert.Equal(["awg1"], await doors.KeptOutAsync(on, configs, CancellationToken.None));
        Assert.Empty(await doors.KeptOutAsync(off, configs, CancellationToken.None));
        var asked = tools.Calls.Count;
        Assert.Equal(["awg1"], await doors.KeptOutAsync(on, configs, CancellationToken.None));
        Assert.Equal(asked, tools.Calls.Count);
        clock.Pass(TimeSpan.FromSeconds(16));
        Assert.Equal(["awg1"], await doors.KeptOutAsync(on, configs, CancellationToken.None));
        Assert.True(tools.Calls.Count > asked);
    }

    private static async Task<byte[]> Ask(DnsResolver resolver, string name, DnsRecordType type = DnsRecordType.A, IPAddress? client = null) =>
        (await resolver.AnswerAsync(DnsBuilder.Question(3, name, type), false, client ?? Asking, CancellationToken.None))!;

    private static (DnsResolver Resolver, DnsStandIns StandIns, Ledger Host) Resolver(IDnsUpstream upstream)
    {
        var host = new Ledger();
        var standIns = new DnsStandIns();
        var sets = new DnsSets(host, null, DnsDefaults.StandingAddresses, standIns);
        var plan = Plan([]);
        var resolver = new DnsResolver(
            upstream,
            new DnsCache(16),
            sets,
            () => plan,
            On,
            new DnsState(),
            ct => sets.FlushAsync(plan, On.NameLifetime, ct),
            null,
            standIns,
            client => client.Equals(Asking) ? Names("googlevideo.com", "youtube.com") : null);

        return (resolver, standIns, host);
    }

    private static DomainMatcher Names(params string[] domains) =>
        new([.. domains.Select(domain => new GeoDomain(GeoDomainKind.Domain, domain))]);

    private static RoutePlan Plan(RouteRule[] rules) =>
        RoutePlan.Build(rules, Ways, GeoIndex.Load([], new MemoryGeoFiles()), ["awg1"], On) with { StandIns = true };

    private static GeoIndex Index()
    {
        var files = new MemoryGeoFiles();
        files.Put("ip", GeoBuilder.Ip(("RU", ["77.88.8.0/24"])));
        files.Put("site", GeoBuilder.Site(("YANDEX", ["yandex.ru", "ya.ru"])));

        return GeoIndex.Load(
            [
                new GeoSource { Name = "ip", Kind = GeoKind.Ip, Position = 1 },
                new GeoSource { Name = "site", Kind = GeoKind.Site, Position = 2 },
            ],
            files);
    }

    private static ServerConfig Endpoint() => new()
    {
        Name = "awg1",
        Host = "vpn.example.org",
        ListenPort = 51820,
        Address = ["10.8.0.1/24"],
        Dns = ["1.1.1.1"],
        AllowedIps = ["0.0.0.0/0", "::/0"],
        Mtu = 1420,
        Keepalive = 25,
        PrivateKey = "kEnCIRhKjNllB2d7J7t5gMAySOXbC9KKrcPwIbkgGUc=",
        PublicKey = "6isSFA1FOVjJl9Ub1VvoJ8OKXFgLPFcwJjzLU+AWSlE=",
    };

    private static TunnelClient Client() => new()
    {
        ConfigId = 1,
        Name = "anna",
        PrivateKey = "qA3cEwnAVQIDQTRPDGmDN6fzrDwARw2mnJ4jswPibnY=",
        PublicKey = "eyf4cmoVSwDUFfS+15aWOaz3jYFQ1pPraV/WkHu3zDk=",
        Address = ["10.8.0.2/32"],
    };

    // Answers the names it holds with one address each and a name it does not hold as one that is not.
    private sealed class Book(params (string Name, string Address)[] book) : IDnsUpstream
    {
        public List<string> Asked { get; } = [];

        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            var asked = DnsMessage.Read(question.Span)!;
            Asked.Add(asked.Question);
            var found = book.Where(one => string.Equals(one.Name, asked.Question, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (found.Length == 0)
            {
                var missing = DnsBuilder.Answer(asked.Id, asked.Question, asked.Type);
                missing[3] = 0x83;

                return Task.FromResult<byte[]?>(missing);
            }

            var told = found.Select(one => new Told(asked.Question, IPAddress.Parse(one.Address).AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? DnsRecordType.A : DnsRecordType.Aaaa, 600, one.Address)).ToArray();

            return Task.FromResult<byte[]?>(DnsBuilder.Answer(asked.Id, asked.Question, asked.Type, told));
        }
    }

    // Answers a name through an alias that leads to a second name with one address.
    private sealed class Alias(string name, string target, string address) : IDnsUpstream
    {
        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            var asked = DnsMessage.Read(question.Span)!;

            return Task.FromResult<byte[]?>(DnsBuilder.Answer(
                asked.Id,
                name,
                asked.Type,
                new Told(name, DnsRecordType.Cname, 600, target),
                new Told(target, DnsRecordType.A, 600, address)));
        }
    }
}
