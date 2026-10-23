using System.Globalization;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Cli;
using AmneziaGeo.Server.Cli.Commands;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Tests;

[Collection(PanelCommandTests.Utility)]
public sealed class TemplateCommandTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-template-");

    public TemplateCommandTests()
    {
        Environment.SetEnvironmentVariable(Context.KeyVariable, Path.Combine(_folder.FullName, "signing.pem"));
    }

    private string Database => Path.Combine(_folder.FullName, "server.db");

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task TheListNamesTheClientsThePresetsAndTheMarkOfEachTemplate()
    {
        var unblock = await AddPreset("Unblock");
        var ads = await AddPreset("No ads");
        await AddTemplate(new ClientTemplate { Name = "phones", AllowedIps = ["0.0.0.0/0"], Presets = [unblock, ads], PresetsOnly = true });

        var said = await Said("template", "list");

        Assert.Equal(
            [
                "default: clients 0, routing presets 0",
                "phones: clients 0, routing presets 2 (Unblock, No ads), presets only",
            ],
            said);
    }

    [Fact]
    public async Task SettingTheMarkKeepsTheRestOfTheTemplate()
    {
        var unblock = await AddPreset("Unblock");
        var id = await AddTemplate(new ClientTemplate { Name = "office phones", AllowedIps = ["198.51.100.0/24"], Presets = [unblock] });

        var kept = await Said("template", "set", "office phones", "--presets-only", "on");
        var read = await Read(id);
        var freed = await Said("template", "set", "office phones", "--presets-only", "off");

        Assert.Equal(["office phones: the applications of its clients route its configurations by its routing presets alone"], kept);
        Assert.True(read.PresetsOnly);
        Assert.Equal(["198.51.100.0/24"], read.AllowedIps);
        Assert.Equal([unblock], read.Presets);
        Assert.Equal(["office phones: the applications of its clients may route by lists of their own"], freed);
        Assert.False((await Read(id)).PresetsOnly);
    }

    [Theory]
    [InlineData("template", "set", "bare", "--presets-only", "on")]
    [InlineData("template", "set", "missing", "--presets-only", "on")]
    [InlineData("template", "set", "bare", "--presets-only", "maybe")]
    [InlineData("template", "set", "bare")]
    [InlineData("template", "drop", "bare")]
    public async Task AWrongLineIsRefused(params string[] words)
    {
        var id = await AddTemplate(new ClientTemplate { Name = "bare", AllowedIps = ["0.0.0.0/0"] });

        Assert.Equal(2, await Code(words));
        Assert.False((await Read(id)).PresetsOnly);
    }

    private async Task<long> AddPreset(string name)
    {
        using var context = Context.Open(Database);
        var added = await context.Presets.AddAsync(new RoutingPreset { Name = name, Proxy = ["geosite:youtube"] }, CancellationToken.None);
        Assert.True(added.IsOk, added.Message);

        return added.Record!.Id;
    }

    private async Task<long> AddTemplate(ClientTemplate draft)
    {
        using var context = Context.Open(Database);
        var added = await context.Templates.AddAsync(draft, CancellationToken.None);
        Assert.True(added.IsOk, added.Message);

        return added.Record!.Id;
    }

    private async Task<ClientTemplate> Read(long id)
    {
        using var context = Context.Open(Database);

        return (await context.Templates.FindAsync(id, CancellationToken.None))!;
    }

    // Runs one command and returns the lines it printed.
    private async Task<string[]> Said(params string[] words)
    {
        var before = Console.Out;
        using var heard = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetOut(heard);
        try
        {
            using var context = Context.Open(Database);
            Assert.Equal(0, await TemplateCommands.RunAsync(context, new Arguments(words), CancellationToken.None));
        }
        finally
        {
            Console.SetOut(before);
        }

        return [.. heard.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    // Runs one command and returns its exit code.
    private async Task<int> Code(params string[] words)
    {
        var before = Console.Error;
        using var heard = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetError(heard);
        try
        {
            using var context = Context.Open(Database);

            return await TemplateCommands.RunAsync(context, new Arguments(words), CancellationToken.None);
        }
        finally
        {
            Console.SetError(before);
        }
    }
}
