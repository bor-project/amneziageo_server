using AmneziaGeo.Server.Cli;
using AmneziaGeo.Server.Cli.Commands;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

public sealed class PanelCommandTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-panel-");

    public PanelCommandTests()
    {
        Environment.SetEnvironmentVariable(Context.KeyVariable, Path.Combine(_folder.FullName, "signing.pem"));
    }

    private string Database => Path.Combine(_folder.FullName, "server.db");

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task AChangeBeforeTheFirstStartLeavesThePanelOnTheLoopbackUnderAFreshPath()
    {
        Assert.Equal(0, await Run("panel", "set", "--channel", "test"));

        var held = PanelStore.Held(Database);

        Assert.NotNull(held);
        Assert.Equal(["127.0.0.1"], held.Listen);
        Assert.Equal(8443, held.Port);
        Assert.Matches("^sub/[a-z0-9]{16}$", held.Path);
    }

    [Fact]
    public async Task AChangeBeforeTheFirstStartChangesOnlyWhatItNames()
    {
        Assert.Equal(0, await Run("panel", "set", "--port", "9443"));
        var first = PanelStore.Held(Database);
        Assert.Equal(0, await Run("panel", "set", "--channel", "test"));
        var second = PanelStore.Held(Database);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(["127.0.0.1"], second.Listen);
        Assert.Equal(9443, second.Port);
        Assert.Equal(first.Path, second.Path);
    }

    [Fact]
    public async Task ThePathReadBeforeTheFirstStartIsTheOneThePanelStartsUnder()
    {
        Assert.Equal(0, await Run("panel", "get", "path"));
        var first = PanelStore.Held(Database);
        Assert.Equal(0, await Run("panel", "get", "path"));
        var second = PanelStore.Held(Database);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Matches("^sub/[a-z0-9]{16}$", first.Path);
        Assert.Equal(first.Path, second.Path);
    }

    [Fact]
    public async Task APanelAtTheRootWrittenWithASlashTakesAChange()
    {
        using (var context = Context.Open(Database))
        {
            await context.Panel.SeedAsync(PanelDefaults.Settings with { Listen = ["127.0.0.1"], Path = "/" }, CancellationToken.None);
        }

        Assert.Equal(0, await Run("panel", "set", "--channel", "test"));
        Assert.Equal("/", PanelStore.Held(Database)?.Prefix);
    }

    private async Task<int> Run(params string[] words)
    {
        using var context = Context.Open(Database);

        return await PanelCommands.RunAsync(context, new Arguments(words), CancellationToken.None);
    }
}
