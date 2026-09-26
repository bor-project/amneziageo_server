using System.Collections.Concurrent;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AmneziaGeo.Server.Tests;

public sealed class DatabaseSeedTests : IDisposable
{
    private const int Processes = 8;

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-seed-");

    private string Database => Path.Combine(_folder.FullName, "server.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task OutboundsSeededAtOnceLeaveOneDirect()
    {
        await MigrateAsync();

        await AtOnceAsync(services => services.GetRequiredService<OutboundStore>().SeedAsync(CancellationToken.None));

        Assert.Equal(1, await CountAsync(db => db.Outbounds.CountAsync()));
    }

    [Fact]
    public async Task TemplatesSeededAtOnceLeaveOneBuiltIn()
    {
        await MigrateAsync();

        await AtOnceAsync(services => services.GetRequiredService<TemplateStore>().SeedAsync(CancellationToken.None));

        Assert.Equal(1, await CountAsync(db => db.Templates.CountAsync()));
    }

    [Fact]
    public async Task GeoSourcesSeededAtOnceAreAddedOnce()
    {
        await MigrateAsync();

        await AtOnceAsync(services => services.GetRequiredService<GeoStore>().SeedAsync(CancellationToken.None));

        var names = await CountAsync(db => db.GeoSources.Select(source => source.Name).Distinct().CountAsync());
        Assert.True(names > 0);
        Assert.Equal(names, await CountAsync(db => db.GeoSources.CountAsync()));
    }

    [Fact]
    public async Task PanelSettingsSeededAtOnceAreWrittenOnce()
    {
        await MigrateAsync();
        var ports = new ConcurrentBag<int>();
        var next = 9000;

        await AtOnceAsync(async services =>
        {
            var port = Interlocked.Increment(ref next);
            var held = await services.GetRequiredService<PanelStore>()
                .SeedAsync(PanelDefaults.Settings with { Port = port }, CancellationToken.None);
            ports.Add(held.Port);
        });

        Assert.Single(ports.Distinct());
        Assert.Equal(ports.First(), PanelStore.Held(Database)?.Port);
    }

    [Fact]
    public async Task PreparationsThatMeetOnAFreshDatabaseAllGoThrough()
    {
        await AtOnceAsync(services => ServerDatabase.PrepareAsync(services));

        Assert.Equal(1, await CountAsync(db => db.Roles.CountAsync()));
        Assert.Equal(Scopes.All.Length, await CountAsync(db => db.RoleClaims.CountAsync()));
        Assert.Equal(1, await CountAsync(db => db.Outbounds.CountAsync()));
        Assert.Equal(1, await CountAsync(db => db.Templates.CountAsync()));
    }

    private ServiceProvider Provider()
    {
        var options = new AuthOptions();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddServerDatabase(Database, options, Path.Combine(_folder.FullName, "geo"));

        return services.BuildServiceProvider();
    }

    private async Task MigrateAsync()
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    private async Task AtOnceAsync(Func<IServiceProvider, Task> work)
    {
        var providers = Enumerable.Range(0, Processes).Select(_ => Provider()).ToList();
        var scopes = providers.Select(provider => provider.CreateScope()).ToList();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = 0;
        try
        {
            var runs = scopes.Select(scope => Task.Run(async () =>
            {
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.OpenConnectionAsync();
                Interlocked.Increment(ref opened);
                await gate.Task;
                await work(scope.ServiceProvider);
            })).ToList();

            while (Volatile.Read(ref opened) < Processes && runs.TrueForAll(run => !run.IsFaulted))
            {
                await Task.Delay(10);
            }

            gate.SetResult();
            await Task.WhenAll(runs);
        }
        finally
        {
            foreach (var scope in scopes)
            {
                scope.Dispose();
            }

            foreach (var provider in providers)
            {
                await provider.DisposeAsync();
            }
        }
    }

    private async Task<int> CountAsync(Func<AppDbContext, Task<int>> count)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        return await count(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
