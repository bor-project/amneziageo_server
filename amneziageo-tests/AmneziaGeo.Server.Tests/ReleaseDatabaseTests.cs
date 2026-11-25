using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using AmneziaGeo.Server.Api.Updates;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Tests;

public sealed class ReleaseDatabaseTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("ag-release-database-");

    [Fact]
    public void SnapshotsAreBoundToTheExactBuildAndSurviveDifferentMountRoots()
    {
        var data = Directory.CreateDirectory(Path.Combine(_folder.FullName, "data")).FullName;
        Create(data);
        ReleaseDatabases.Save(data, "docker:sha256:first");
        Write(data, "CREATE TABLE RoutingEntries (Id INTEGER); DROP TABLE Rules; UPDATE Canary SET Value = 'new'");
        ReleaseDatabases.Save(data, "docker:sha256:second");
        Assert.Null(ReleaseDatabases.Find(data, "docker:sha256:unrelated"));
        var moved = Path.Combine(_folder.FullName, "other-mount");
        Directory.Move(data, moved);
        var first = ReleaseDatabases.Find(moved, "docker:sha256:first");
        Assert.NotNull(first);
        ReleaseDatabases.Restore(moved, first);
        Assert.Equal("old", Value(moved));
        Assert.True(Table(moved, "Rules"));
        ReleaseDatabases.Restore(moved, ReleaseDatabases.Find(moved, "docker:sha256:second")!);
        Assert.Equal("new", Value(moved));
        Assert.True(Table(moved, "RoutingEntries"));
    }

    [Fact]
    public void PruningKeepsTheBuildMatchedCopiesAlongsideRecentUnboundBackups()
    {
        var data = Directory.CreateDirectory(Path.Combine(_folder.FullName, "data")).FullName;
        Create(data);
        var held = ReleaseDatabases.Save(data, "docker:sha256:first");
        for (var number = 0; number < 8; number++)
        {
            Directory.CreateDirectory(Path.Combine(data, "backup", "z-newer-" + number));
        }
        ReleaseDatabases.Prune(data, "docker:sha256:first");
        Assert.Equal(held, ReleaseDatabases.Find(data, "docker:sha256:first"));
        Assert.Equal(6, Directory.GetDirectories(Path.Combine(data, "backup")).Length);
    }

    [Fact]
    public void APrivilegedHelperPreservesADifferentDatabaseOwnerAndGroup()
    {
        Skip.IfNot(OperatingSystem.IsLinux() && Environment.IsPrivilegedProcess);
        var data = Directory.CreateDirectory(Path.Combine(_folder.FullName, "data")).FullName;
        Create(data);
        var file = Path.Combine(data, "server.db");
        Run("chown", "12345:12346", file);
        Assert.Equal("12345:12346", Run("stat", "-c", "%u:%g", file));
        var snapshot = ReleaseDatabases.Save(data, "docker:sha256:custom-user");
        Assert.Equal("12345:12346", Run("stat", "-c", "%u:%g", Path.Combine(snapshot, "server.db")));
        ReleaseDatabases.Restore(data, snapshot);
        Assert.Equal("12345:12346", Run("stat", "-c", "%u:%g", file));
        Assert.Equal("old", Value(data));
    }

    private static string Run(string name, params string[] args)
    {
        var start = new ProcessStartInfo(name) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }

    [Fact]
    public void SnapshotsAndRestoreStagingArePrivateOnLinux()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        var data = Directory.CreateDirectory(Path.Combine(_folder.FullName, "data")).FullName;
        Create(data);
        var snapshot = ReleaseDatabases.Save(data, "docker:sha256:first");
        ReleaseDatabases.Restore(data, snapshot);
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.Equal(mode, File.GetUnixFileMode(Path.Combine(data, "backup")));
        Assert.Equal(mode, File.GetUnixFileMode(Path.Combine(data, "rollback")));
        Assert.All(Directory.GetDirectories(Path.Combine(data, "backup")), folder => Assert.Equal(mode, File.GetUnixFileMode(folder)));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(data, "server.db")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DockerTakesItsSnapshotAfterStopAndRestoresItBeforeRestartingTheOldImage(bool healthy)
    {
        var data = Directory.CreateDirectory(Path.Combine(_folder.FullName, "data")).FullName;
        Create(data);
        var compose = Directory.CreateDirectory(Path.Combine(_folder.FullName, "compose")).FullName;
        File.WriteAllText(Path.Combine(compose, ".env"), "AMNEZIAGEO_TAG=1.0.0.0\nOTHER=kept\n");
        using var daemon = new Daemon(data, healthy);
        using var http = new HttpClient(daemon) { BaseAddress = new Uri("http://docker/") };
        using var engine = new DockerEngine(http);
        var result = await HandoverCommand.MoveAsync(engine,
            new HandoverCommand.Handover("fixture", "panel", compose, "registry/panel:2.0.0.0", "1.0.0.0", "2.0.0.0", data), default).ConfigureAwait(false);
        Assert.Equal(healthy ? 0 : 1, result);
        Assert.True(daemon.Stopped);
        var backup = ReleaseDatabases.Find(data, "docker:sha256:old");
        Assert.NotNull(backup);
        Assert.Equal("final-before-stop", Value(backup));
        Assert.Equal(healthy ? "new" : "final-before-stop", Value(data));
        Assert.True(Table(data, healthy ? "RoutingEntries" : "Rules"));
        Assert.Equal(!healthy, daemon.OldRestarted);
        Assert.Contains("AMNEZIAGEO_TAG=" + (healthy ? "2.0.0.0" : "1.0.0.0"), File.ReadAllText(Path.Combine(compose, ".env")));
        Assert.Contains("OTHER=kept", File.ReadAllText(Path.Combine(compose, ".env")));
    }

    private sealed class Daemon : HttpMessageHandler
    {
        private readonly string _data;
        private readonly bool _healthy;
        public bool Stopped { get; private set; }
        public bool OldRestarted { get; private set; }

        public Daemon(string data, bool healthy)
        {
            _data = data;
            _healthy = healthy;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var result = "{}";
            if (path == "/containers/json")
            {
                result = "[{\"Id\":\"old\"}]";
            }
            else if (path == "/containers/panel-1.0.0.0/json")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            else if (path == "/containers/old/json")
            {
                result = """{"Name":"/panel","Image":"sha256:old","Config":{"Image":"registry/panel:1.0.0.0","Env":[]},"HostConfig":{"NetworkMode":"host"},"Mounts":[]}""";
            }
            else if (path.StartsWith("/images/", StringComparison.Ordinal))
            {
                Assert.Contains("sha256", path, StringComparison.Ordinal);
                Assert.DoesNotContain("registry", path, StringComparison.Ordinal);
                result = """{"Id":"sha256:old","Config":{}}""";
            }
            else if (path == "/containers/old/stop")
            {
                Write(_data, "UPDATE Canary SET Value = 'final-before-stop'");
                Stopped = true;
            }
            else if (path == "/containers/create")
            {
                result = "{\"Id\":\"new\"}";
            }
            else if (path == "/containers/new/start")
            {
                Assert.True(Stopped);
                Write(_data, "DROP TABLE Rules; CREATE TABLE RoutingEntries (Id INTEGER); UPDATE Canary SET Value='new'");
            }
            else if (path == "/containers/new/json")
            {
                result = new JsonObject { ["Image"] = "sha256:new", ["State"] = new JsonObject { ["Health"] = new JsonObject { ["Status"] = _healthy ? "healthy" : "unhealthy" } } }.ToJsonString();
            }
            else if (path == "/containers/old/start")
            {
                Assert.True(Table(_data, "Rules"));
                Assert.Equal("final-before-stop", Value(_data));
                OldRestarted = true;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(result, Encoding.UTF8, "application/json") });
        }
    }

    private static void Create(string data) => Write(data, "CREATE TABLE Rules (Id INTEGER); CREATE TABLE Canary (Value TEXT); INSERT INTO Canary VALUES ('old')");

    private static void Write(string data, string sql)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(data, "server.db")};Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Value(string data)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(data, "server.db")};Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT Value FROM Canary";
        return (string)command.ExecuteScalar()!;
    }

    private static bool Table(string data, string table)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(data, "server.db")};Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return (long)command.ExecuteScalar()! == 1;
    }

    public void Dispose() => _folder.Delete(recursive: true);
}
