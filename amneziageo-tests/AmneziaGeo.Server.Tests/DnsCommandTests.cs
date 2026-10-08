using System.Globalization;
using System.Net;
using System.Net.Sockets;
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
    public async Task AFreshPanelHoldsTheResolverOff()
    {
        Assert.Equal(0, await Run("dns", "show"));
        Assert.Equal(0, await Run("dns", "get", "enabled", "encrypted", "upstreams"));

        var held = await Held();

        Assert.False(held.IsEnabled);
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

        Assert.False(held.IsEnabled);
        Assert.Equal(DnsDefaults.Upstreams, held.Upstreams);
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
