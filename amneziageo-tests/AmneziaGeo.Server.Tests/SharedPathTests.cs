using System.Collections.Concurrent;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public sealed class SharedPathTests
{
    [Fact]
    public async Task EndpointsThatShareAPathOnOnePortTakePathsOfTheirOwnButTheFirst()
    {
        using var bench = new Bench();
        var first = await AddAsync(bench, "awg1", 51820, 8443, true);
        var second = await AddAsync(bench, "awg2", 51821, 8443, true);
        var elsewhere = await AddAsync(bench, "awg3", 51822, 9443, true);
        var off = await AddAsync(bench, "awg4", 51823, 8443, false);
        var third = await AddAsync(bench, "awg5", 51824, 8443, true);
        await PathsAsync(bench, (first, "v1"), (second, "V1"), (elsewhere, "v1"), (off, "v1"), (third, "v1"));

        var split = await bench.Configs.SplitPathsAsync(default);
        var held = await HeldAsync(bench);

        Assert.Equal(new[] { new SplitPath("awg2", "awg1", 8443), new SplitPath("awg5", "awg1", 8443) }, split);
        Assert.Equal("v1", held[first]);
        Assert.Matches("^[a-z0-9]{16}$", held[second]);
        Assert.Matches("^[a-z0-9]{16}$", held[third]);
        Assert.NotEqual(held[second], held[third]);
        Assert.Equal("v1", held[elsewhere]);
        Assert.Equal("v1", held[off]);
        Assert.Empty(await bench.Configs.SplitPathsAsync(default));
        Assert.Equal(held, await HeldAsync(bench));
    }

    [Fact]
    public async Task AnEndpointWithoutAPathSharesTheOldOne()
    {
        using var bench = new Bench();
        var old = await AddAsync(bench, "awg1", 51820, 8443, true);
        var named = await AddAsync(bench, "awg2", 51821, 8443, true);
        await PathsAsync(bench, (old, string.Empty), (named, ConfigServices.OldPath));

        var split = await bench.Configs.SplitPathsAsync(default);
        var held = await HeldAsync(bench);

        Assert.Equal(new SplitPath("awg2", "awg1", 8443), Assert.Single(split));
        Assert.Equal(string.Empty, held[old]);
        Assert.Matches("^[a-z0-9]{16}$", held[named]);
    }

    [Fact]
    public async Task APanelThatStartsOverASharedPathPartsItAndNamesWhoMovedWithoutThePath()
    {
        var folder = Directory.CreateTempSubdirectory("amneziageo-split-");
        var heard = new Heard();
        try
        {
            await using var provider = Provider(Path.Combine(folder.FullName, "server.db"), folder.FullName, heard);
            await ServerDatabase.PrepareAsync(provider);
            using var scope = provider.CreateScope();
            var configs = scope.ServiceProvider.GetRequiredService<ConfigStore>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await configs.AddAsync(ConfigDefaults.Fresh("awg1") with { ListenPort = 51820, ServicesPort = 8443, WebSocket = true }, default);
            await configs.AddAsync(ConfigDefaults.Fresh("awg2") with { ListenPort = 51821, ServicesPort = 8443, WebSocket = true }, default);
            await db.Configs.ExecuteUpdateAsync(set => set.SetProperty(config => config.WebSocketPath, ConfigServices.OldPath));

            await ServerDatabase.PrepareAsync(provider);

            var paths = await db.Configs.AsNoTracking().OrderBy(config => config.Id).Select(config => config.WebSocketPath).ToListAsync();
            Assert.Equal(ConfigServices.OldPath, paths[0]);
            Assert.Matches("^[a-z0-9]{16}$", paths[1]);
            var said = Assert.Single(heard.Lines, line => line.Contains("awg2", StringComparison.Ordinal));
            Assert.StartsWith("Warning: the websocket of awg2", said, StringComparison.Ordinal);
            Assert.Contains("awg1", said, StringComparison.Ordinal);
            Assert.Contains("8443", said, StringComparison.Ordinal);
            Assert.DoesNotContain(paths[1], said, StringComparison.Ordinal);
        }
        finally
        {
            Pools.ClearUnder(folder.FullName);
            folder.Delete(recursive: true);
        }
    }

    private static async Task<long> AddAsync(Bench bench, string name, int listen, int services, bool socket) =>
        (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh(name) with { ListenPort = listen, ServicesPort = services, WebSocket = socket },
            default)).Record!.Id;

    private static async Task PathsAsync(Bench bench, params (long Id, string Path)[] paths)
    {
        foreach (var (id, path) in paths)
        {
            var entity = await bench.Db.Configs.SingleAsync(config => config.Id == id);
            entity.WebSocketPath = path;
        }

        await bench.Db.SaveChangesAsync();
    }

    private static Task<Dictionary<long, string>> HeldAsync(Bench bench) =>
        bench.Db.Configs.AsNoTracking().ToDictionaryAsync(config => config.Id, config => config.WebSocketPath);

    private static ServiceProvider Provider(string database, string folder, Heard heard)
    {
        var options = new AuthOptions();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(heard));
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddServerDatabase(database, options, Path.Combine(folder, "geo"));

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
