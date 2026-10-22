using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Cli;
using AmneziaGeo.Server.Cli.Commands;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

[Collection(PanelCommandTests.Utility)]
public sealed class DnsCommandTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-dns-");

    public DnsCommandTests()
    {
        Environment.SetEnvironmentVariable(Context.KeyVariable, Path.Combine(_folder.FullName, "signing.pem"));
        File.WriteAllText(
            Path.Combine(_folder.FullName, "health"),
            string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{Free()}/api/health"));
    }

    private string Database => Path.Combine(_folder.FullName, "server.db");

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task AFreshPanelRunsTheResolverWhileTheRoutingNeedsIt()
    {
        Assert.Equal(0, await Run("dns", "show"));
        Assert.Equal(0, await Run("dns", "get", "enabled", "encrypted", "upstreams"));

        var held = await Held();

        Assert.True(held.IsEnabled);
        Assert.Equal(DnsDefaults.Upstreams, held.Upstreams);
    }

    [Fact]
    public async Task TurningTheResolverOnWritesItDownWithTheNameServersAskedEncrypted()
    {
        Assert.Equal(0, await Run("dns", "set", "--enabled", "on"));

        var held = await Held();

        Assert.True(held.IsEnabled);
        Assert.Equal(DnsDefaults.Upstreams, held.Upstreams);
        Assert.All(held.Upstreams, one => Assert.True(DnsRules.Server(one, out var server) && server.IsEncrypted));
    }

    [Fact]
    public async Task TurningTheResolverOffKeepsItsNameServers()
    {
        Assert.Equal(0, await Run("dns", "set", "--enabled", "on", "--upstreams", "tls://9.9.9.9,https://1.0.0.1/dns-query"));
        Assert.Equal(0, await Run("dns", "set", "--enabled", "off"));

        var held = await Held();

        Assert.False(held.IsEnabled);
        Assert.Equal(["tls://9.9.9.9", "https://1.0.0.1/dns-query"], held.Upstreams);
    }

    [Fact]
    public async Task TheNameServersAPanelStartsWithComeBackByTheirWord()
    {
        Assert.Equal(0, await Run("dns", "set", "--upstreams", "1.1.1.1"));
        var plain = await Held();
        Assert.Equal(0, await Run("dns", "set", "--upstreams", DnsCommands.Usual));
        var usual = await Held();

        Assert.Equal(["1.1.1.1"], plain.Upstreams);
        Assert.Equal(DnsDefaults.Upstreams, usual.Upstreams);
    }

    [Fact]
    public async Task TheFilesOfTheClientsNameWhatTheServerPicksAndItsOwnServersComeWithIt()
    {
        var usual = await Heard("dns", "get", "files");
        Assert.Equal(0, await Run("dns", "set", "--files", "own", "--files-dns", "9.9.9.9,149.112.112.112"));
        var own = await Held();
        var shown = await Heard("dns", "show");
        Assert.Equal(0, await Run("dns", "set", "--files", "network"));
        var network = await Held();
        var named = await Heard("dns", "get", "files", "files-dns");

        Assert.Equal(["routing"], usual);
        Assert.Equal("Own 9.9.9.9,149.112.112.112", $"{own.FilesMode} {string.Join(",", own.FilesDns)}");
        Assert.Contains("for clients  own name servers 9.9.9.9, 149.112.112.112", shown);
        Assert.Equal(DnsChoice.Network, network.FilesMode);
        Assert.Equal(["network", "9.9.9.9,149.112.112.112"], named);
        Assert.True(network.IsEnabled);
    }

    [Theory]
    [InlineData("dns", "set", "--files", "own")]
    [InlineData("dns", "set", "--files", "dhcp")]
    [InlineData("dns", "set", "--files", "own", "--files-dns", "dns.quad9.net")]
    public async Task AChoiceOfTheFilesTheServerDoesNotTakeIsRefused(params string[] words)
    {
        Assert.Equal(2, await Run(words));

        Assert.Equal(DnsChoice.Routing, (await Held()).FilesMode);
    }

    [Theory]
    [InlineData("dns", "set")]
    [InlineData("dns", "set", "--enabled", "maybe")]
    [InlineData("dns", "set", "--upstreams", "https://dns.google/dns-query")]
    [InlineData("dns", "get")]
    [InlineData("dns", "get", "nonsense")]
    [InlineData("dns", "what")]
    public async Task ACommandThatAsksForNothingKnownIsRefusedAndChangesNothing(params string[] words)
    {
        Assert.Equal(2, await Run(words));

        var held = await Held();

        Assert.True(held.IsEnabled);
        Assert.Equal(DnsDefaults.Upstreams, held.Upstreams);
    }

    [Fact]
    public async Task TheClientsWhoseFilesNameTheResolverAreCountedAndNamedWhenItIsTurnedOff()
    {
        using (var context = Context.Open(Database))
        {
            var endpoint = (await context.Configs.AddAsync(
                ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
                CancellationToken.None)).Record!.Id;
            var anna = (await context.Clients.AddAsync(
                ClientDefaults.Fresh(endpoint, "anna") with { Address = ["10.8.0.2/32"] },
                CancellationToken.None)).Record!.Id;
            var guest = (await context.Clients.AddAsync(
                ClientDefaults.Fresh(endpoint, "guest") with { Address = ["10.8.0.3/32"] },
                CancellationToken.None)).Record!.Id;
            await context.Clients.NoteResolverAsync([anna, guest], [], CancellationToken.None);
        }

        var counted = await Heard("dns", "get", "holders");
        var shown = await Heard("dns", "show");
        var off = await Heard("dns", "set", "--enabled", "off");

        Assert.Equal(["2"], counted);
        Assert.Contains("files        2 clients hold files that name the resolver", shown);
        Assert.Contains(off, line => line.StartsWith("2 clients hold files that name the resolver: their names go unanswered", StringComparison.Ordinal));
    }

    // Runs one command and returns the lines it printed.
    private async Task<IReadOnlyList<string>> Heard(params string[] words)
    {
        var before = Console.Out;
        using var heard = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetOut(heard);
        try
        {
            Assert.Equal(0, await Run(words));
        }
        finally
        {
            Console.SetOut(before);
        }

        return heard.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<int> Run(params string[] words)
    {
        using var context = Context.Open(Database);

        return await DnsCommands.RunAsync(context, new Arguments(words), CancellationToken.None);
    }

    private async Task<DnsSettings> Held()
    {
        using var context = Context.Open(Database);

        return await context.Dns.ReadAsync(CancellationToken.None);
    }

    // Returns a loopback port nobody listens on, so a command finds no panel to call.
    private static int Free()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }
}
