using AmneziaGeo.Server.Dal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AmneziaGeo.Server.Tests;

public class DatabaseOpeningTests
{
    private const int Openers = 8;

    private const int Rounds = 3000;

    [Fact]
    public void ConnectionsOpenedAtOnceNeverShareOneConnectionOfSqlite()
    {
        var folder = Directory.CreateTempSubdirectory("amneziageo-opening-");
        var text = $"Data Source={Path.Combine(folder.FullName, "one.db")}";
        try
        {
            var shared = 0;
            for (var round = 0; round < Rounds; round++)
            {
                using (var clear = new SqliteConnection(text))
                {
                    SqliteConnection.ClearPool(clear);
                }

                var connections = new SqliteConnection[Openers];
                using var start = new Barrier(Openers);
                var threads = Enumerable.Range(0, Openers).Select(at => new Thread(() =>
                {
                    var connection = new SqliteConnection(text);
                    start.SignalAndWait();
                    DatabaseOpening.Open(connection);
                    connections[at] = connection;
                })).ToArray();
                foreach (var thread in threads)
                {
                    thread.Start();
                }

                foreach (var thread in threads)
                {
                    thread.Join();
                }

                if (connections.Select(one => one.Handle).Distinct().Count() < Openers)
                {
                    shared++;
                }

                foreach (var connection in connections)
                {
                    connection.Dispose();
                }
            }

            Assert.Equal(0, shared);
        }
        finally
        {
            Pools.ClearUnder(folder.FullName);
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void AContextOfThePanelOpensThroughTheTurn()
    {
        using var bench = new Bench();

        var interceptors = bench.Db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

        Assert.Contains(DatabaseOpening.Instance, interceptors);
    }

    [Fact]
    public async Task AConnectionThatNeverTookTheTurnDoesNotLetItGo()
    {
        using var first = new SqliteConnection("Data Source=:memory:");
        using var stray = new SqliteConnection("Data Source=:memory:");
        using var second = new SqliteConnection("Data Source=:memory:");

        DatabaseOpening.Instance.ConnectionOpening(first, null!, default);
        var waiting = Task.CompletedTask;
        var held = false;
        try
        {
            DatabaseOpening.Instance.ConnectionFailed(stray, null!);
            waiting = Task.Run(() => DatabaseOpening.Instance.ConnectionOpening(second, null!, default));
            await Task.Delay(200);
            held = !waiting.IsCompleted;
        }
        finally
        {
            DatabaseOpening.Instance.ConnectionOpened(first, null!);
            await waiting.WaitAsync(TimeSpan.FromSeconds(5));
            DatabaseOpening.Instance.ConnectionOpened(second, null!);
        }

        Assert.True(held);
    }
}
