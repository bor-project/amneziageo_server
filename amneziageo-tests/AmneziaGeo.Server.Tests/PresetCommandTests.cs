using System.Globalization;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Cli;
using AmneziaGeo.Server.Cli.Commands;
using AmneziaGeo.Server.Routing.Template;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Tests;

[Collection(PanelCommandTests.Utility)]
public sealed class PresetCommandTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-preset-");

    public PresetCommandTests()
    {
        Environment.SetEnvironmentVariable(Context.KeyVariable, Path.Combine(_folder.FullName, "signing.pem"));
    }

    private string Database => Path.Combine(_folder.FullName, "server.db");

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task TheListNamesWhatEachPresetCarriesAndItsMarks()
    {
        var unblock = await Add(new RoutingPreset { Name = "Unblock", Proxy = ["geosite:youtube", "geoip:ru"], Direct = ["domain:example.org"], IsDefault = true, ReadOnly = true });
        await Add(new RoutingPreset { Name = "No ads", Block = ["geosite:category-ads-all"] });
        await AddTemplate("phones", unblock);

        var said = await Said("preset", "list");

        Assert.Equal(
            [
                "No ads: through the tunnel 0, directly 0, blocked 1, templates 0",
                "Unblock: through the tunnel 2, directly 1, blocked 0, templates 1, on by default, read-only",
            ],
            said);
    }

    [Fact]
    public async Task TheListMarksAPresetMadeToTunnelEverything()
    {
        var id = await Add(new RoutingPreset { Name = "All", Direct = ["geoip:ru"] });
        await MarkFull(id);

        var said = await Said("preset", "list");

        Assert.Equal(["All: through the tunnel 0, directly 1, blocked 0, templates 0, everything through the tunnel: unsafe for the server, turn it off in the panel"], said);
    }

    [Fact]
    public async Task SettingTheMarkKeepsTheListAndItsTime()
    {
        var id = await Add(new RoutingPreset { Name = "Unblock list", Proxy = ["geosite:youtube"] });
        var before = await Read(id);

        var kept = await Said("preset", "set", "Unblock list", "--readonly", "on");
        var read = await Read(id);
        var freed = await Said("preset", "set", "Unblock list", "--readonly", "off");

        Assert.Equal(["Unblock list: the applications of the clients keep the list read-only and take its versions as they come"], kept);
        Assert.True(read.ReadOnly);
        Assert.Equal(before.Proxy, read.Proxy);
        Assert.Equal(before.UpdatedUtc, read.UpdatedUtc);
        Assert.Equal(["Unblock list: the applications of the clients may change the list"], freed);
        Assert.False((await Read(id)).ReadOnly);
    }

    [Theory]
    [InlineData("preset", "set", "Missing", "--readonly", "on")]
    [InlineData("preset", "set", "Unblock", "--readonly", "maybe")]
    [InlineData("preset", "set", "Unblock")]
    [InlineData("preset", "drop", "Unblock")]
    public async Task AWrongLineIsRefused(params string[] words)
    {
        await Add(new RoutingPreset { Name = "Unblock", Proxy = ["geosite:youtube"] });

        Assert.Equal(2, await Code(words));
        Assert.False((await ReadByName("Unblock")).ReadOnly);
    }

    private async Task<long> Add(RoutingPreset draft)
    {
        using var context = Context.Open(Database);
        var added = await context.Presets.AddAsync(draft, CancellationToken.None);
        Assert.True(added.IsOk, added.Message);

        return added.Record!.Id;
    }

    // Marks a preset the way the panel made it before the mode was retired.
    private async Task MarkFull(long id)
    {
        await using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE RoutingPresets SET Full = 1 WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task AddTemplate(string name, params long[] presets)
    {
        using var context = Context.Open(Database);
        var added = await context.Templates.AddAsync(
            new ClientTemplate { Name = name, AllowedIps = ["0.0.0.0/0"], Presets = presets },
            CancellationToken.None);
        Assert.True(added.IsOk, added.Message);
    }

    private async Task<RoutingPreset> Read(long id)
    {
        using var context = Context.Open(Database);

        return (await context.Presets.FindAsync(id, CancellationToken.None))!;
    }

    private async Task<RoutingPreset> ReadByName(string name)
    {
        using var context = Context.Open(Database);

        return (await context.Presets.ListAsync(CancellationToken.None)).Single(one => one.Name == name);
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
            Assert.Equal(0, await PresetCommands.RunAsync(context, new Arguments(words), CancellationToken.None));
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

            return await PresetCommands.RunAsync(context, new Arguments(words), CancellationToken.None);
        }
        finally
        {
            Console.SetError(before);
        }
    }
}
