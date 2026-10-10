using System.Globalization;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Cli;
using AmneziaGeo.Server.Cli.Commands;

namespace AmneziaGeo.Server.Tests;

[Collection(PanelCommandTests.Utility)]
public sealed class EndpointCommandTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-endpoint-");

    public EndpointCommandTests()
    {
        Environment.SetEnvironmentVariable(Context.KeyVariable, Path.Combine(_folder.FullName, "signing.pem"));
    }

    private string Database => Path.Combine(_folder.FullName, "server.db");

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task TheListNamesWhoKeepsThePortOfAnEndpointOpen()
    {
        await Add(ConfigDefaults.Fresh("awg0") with { ListenPort = 51821, WebSocket = true }, opened: false);
        await Add(ConfigDefaults.Fresh("awg1") with { ListenPort = 443, Address = ["10.0.1.1/24"] }, opened: true);

        var said = await Said("endpoint", "list");

        Assert.Equal(
            [
                "awg0: port 51821, left to the host, websocket on TCP 51821",
                "awg1: port 443, held open by the panel",
            ],
            said);
    }

    [Fact]
    public async Task OpeningAndClosingSayWhoKeepsThePortFromNowOn()
    {
        await Add(ConfigDefaults.Fresh("awg0"), opened: false);

        var opened = await Said("endpoint", "open", "awg0");
        var closed = await Said("endpoint", "close", "awg0");

        Assert.Equal(["awg0: the port is held open by the panel, it takes hold when the service restarts"], opened);
        Assert.Equal(["awg0: the port is left to the host, it takes hold when the service restarts"], closed);
    }

    private async Task Add(ServerConfig draft, bool opened)
    {
        using var context = Context.Open(Database);
        var added = await context.Configs.AddAsync(draft, CancellationToken.None);
        Assert.True(added.IsOk, added.Message);
        var held = await context.Configs.OpenedAsync(added.Record!.Id, opened, CancellationToken.None);
        Assert.True(held.IsOk, held.Message);
    }

    // Runs one command and returns the lines it said about the endpoints.
    private async Task<string[]> Said(params string[] words)
    {
        var before = Console.Out;
        using var heard = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetOut(heard);
        try
        {
            using var context = Context.Open(Database);
            Assert.Equal(0, await EndpointCommands.RunAsync(context, new Arguments(words), CancellationToken.None));
        }
        finally
        {
            Console.SetOut(before);
        }

        return
        [
            .. heard.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("awg", StringComparison.Ordinal)),
        ];
    }
}
