using System.Collections.Concurrent;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public sealed class RuleSourcesTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-rule-sources-");

    public void Dispose()
    {
        Pools.ClearUnder(_folder.FullName);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task TheAddressesOfARuleBecomeTheClientsStandingThereAndARuleLeftWithoutClientsIsTurnedOff()
    {
        var heard = new Heard();
        await using var provider = Provider(heard);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.GetService<IMigrator>().MigrateAsync("DropRuleHold");
            var configs = scope.ServiceProvider.GetRequiredService<ConfigStore>();
            var office = (await configs.AddAsync(ConfigDefaults.Fresh("awg1") with { Address = ["10.9.1.1/24"] }, default)).Record!.Id;
            var guests = (await configs.AddAsync(
                ConfigDefaults.Fresh("awg2") with { ListenPort = 51821, Address = ["10.9.2.1/24", "fd00::9:1/112"] },
                default)).Record!.Id;
            await AddAsync(db, office, "bor", "10.9.1.12/32");
            await AddAsync(db, office, "bor-phone", "10.9.1.13/32");
            await AddAsync(db, guests, "guest", "10.9.2.5/32", "fd00::9:5/128");
            await RuleAsync(db, 1, "one", true, "10.9.1.12", string.Empty);
            await RuleAsync(db, 2, "office", true, "10.9.1.0/24", string.Empty);
            await RuleAsync(db, 3, "nobody", true, "10.7.0.0/24", string.Empty);
            await RuleAsync(db, 4, "mixed", true, "10.7.0.0/24, fd00::9:0/112", "BOR");
            await RuleAsync(db, 5, "plain", true, string.Empty, string.Empty);
            await RuleAsync(db, 6, "asleep", false, "10.7.0.0/24", string.Empty);
        }

        await ServerDatabase.PrepareAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rules = (await scope.ServiceProvider.GetRequiredService<RouteStore>().ListAsync(default)).ToDictionary(rule => rule.Name);
            var sources = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('Rules') WHERE name = 'Sources'")
                .ToListAsync();
            var left = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name = 'RuleSourcesLeft'")
                .ToListAsync();
            Assert.Equal([0], sources);
            Assert.Equal([0], left);
            Holds(rules["one"], true, "bor");
            Holds(rules["office"], true, "bor", "bor-phone");
            Holds(rules["nobody"], false);
            Holds(rules["mixed"], true, "BOR", "guest");
            Holds(rules["plain"], true);
            Holds(rules["asleep"], false);
        }

        var said = heard.Lines.Where(line => line.Contains("the rule ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(5, said.Length);
        Assert.All(said, line => Assert.StartsWith("Warning: ", line, StringComparison.Ordinal));
        Assert.Contains(said, line => line.Contains("'office'", StringComparison.Ordinal) && line.Contains("bor, bor-phone", StringComparison.Ordinal));
        Assert.Contains(said, line => line.Contains("'nobody'", StringComparison.Ordinal) && line.Contains("turned off", StringComparison.Ordinal));
        Assert.DoesNotContain(said, line => line.Contains("'plain'", StringComparison.Ordinal));

        await ServerDatabase.PrepareAsync(provider);

        Assert.Equal(5, heard.Lines.Count(line => line.Contains("the rule ", StringComparison.Ordinal)));
    }

    [Fact]
    public void ARuleWhoseAddressesCoverMoreClientsThanARuleNamesIsTurnedOffWithItsOwnClients()
    {
        var clients = Enumerable.Range(1, RouteRules.MaxConditions)
            .Select(at => ($"c{at}", (IReadOnlyList<string>)[$"10.8.{at / 250}.{(at % 250) + 2}/32"]))
            .ToArray();

        var move = RuleSources.Move("wide", ["10.8.0.0/16"], ["bor"], true, clients);
        var fits = RuleSources.Move("wide", ["10.8.0.0/16"], [], true, clients);

        Assert.True(move.IsTooMany);
        Assert.False(move.IsEnabled);
        Assert.Equal(["bor"], move.Clients);
        Assert.Empty(move.Added);
        Assert.Equal(RouteRules.MaxConditions, move.Standing);
        Assert.False(fits.IsTooMany);
        Assert.True(fits.IsEnabled);
        Assert.Equal(RouteRules.MaxConditions, fits.Clients.Count);
    }

    private static void Holds(RouteRule rule, bool on, params string[] clients)
    {
        Assert.Equal(clients, rule.Clients);
        Assert.Equal(on, rule.IsEnabled);
    }

    private static Task AddAsync(AppDbContext db, long config, string name, params string[] address) =>
        db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Clients (Address, ConfigId, CreatedUtc, DailyLimit, Inbound, IsEnabled, Name, Note, PresharedKey, PrivateKey, PublicKey, SubscriptionId, TemplateId, UpdatedUtc) "
            + "VALUES ({0}, {1}, '2026-10-19 10:00:00+00:00', 0, 0, 1, {2}, '', '', '', {3}, '', NULL, '2026-10-19 10:00:00+00:00')",
            string.Join(", ", address),
            config,
            name,
            "key-" + name);

    private static Task RuleAsync(AppDbContext db, int position, string name, bool on, string sources, string clients) =>
        db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Rules (Name, Position, IsEnabled, Action, Outbound, Targets, Sources, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc) "
            + "VALUES ({0}, {1}, {2}, 'block', '', '77.88.8.0/24', {3}, {4}, '', '', '', 'any', '2026-10-19 10:00:00+00:00', '2026-10-19 10:00:00+00:00')",
            name,
            position,
            on,
            sources,
            clients);

    private ServiceProvider Provider(Heard heard)
    {
        var options = new AuthOptions();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(heard));
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddServerDatabase(Path.Combine(_folder.FullName, "server.db"), options, Path.Combine(_folder.FullName, "geo"));

        return services.BuildServiceProvider();
    }

    private sealed class Heard : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _lines.Enqueue($"{logLevel}: {formatter(state, exception)}");

        public void Dispose()
        {
        }
    }
}
