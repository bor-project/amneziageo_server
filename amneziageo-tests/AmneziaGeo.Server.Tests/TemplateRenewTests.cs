using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public class TemplateRenewTests
{
    [Fact]
    public async Task RangesADayOldAreWorkedOutAnew()
    {
        using var bench = new Bench();
        using var server = new NameServer(("example.com", "93.184.216.34"));
        var template = await StaleAsync(bench, TimeSpan.FromDays(2), "10.0.0.0/8", "domain:example.com");

        var renewed = await Renewer(bench, server.Point).RenewAsync(TemplateSchedule.Age, CancellationToken.None);
        var read = await bench.Templates.FindAsync(template.Id, CancellationToken.None);

        Assert.Equal(["blocked"], renewed);
        Assert.Equal(["10.0.0.0/8", "93.184.216.34/32"], read!.AllowedIps);
        Assert.Empty(read.Missed);
        Assert.Equal(bench.Clock.Now, read.RefreshedUtc);
        Assert.Equal(template.UpdatedUtc, read.UpdatedUtc);
    }

    [Fact]
    public async Task RangesYoungerThanADayWaitUnlessEveryTemplateIsAskedFor()
    {
        using var bench = new Bench();
        using var server = new NameServer(("example.com", "93.184.216.34"));
        await StaleAsync(bench, TimeSpan.FromHours(23), "domain:example.com");
        var renewer = Renewer(bench, server.Point);

        var aged = await renewer.RenewAsync(TemplateSchedule.Age, CancellationToken.None);
        var all = await renewer.RenewAsync(null, CancellationToken.None);

        Assert.Empty(aged);
        Assert.Equal(["blocked"], all);
    }

    [Fact]
    public async Task SilentNameServersLeaveTheRangesAsTheyWere()
    {
        using var bench = new Bench();
        var template = await StaleAsync(bench, TimeSpan.FromDays(2), "10.0.0.0/8", "domain:example.com");

        var renewed = await Renewer(bench, Closed()).RenewAsync(null, CancellationToken.None);
        var read = await bench.Templates.FindAsync(template.Id, CancellationToken.None);

        Assert.Empty(renewed);
        Assert.Equal(template.AllowedIps, read!.AllowedIps);
        Assert.Equal(template.RefreshedUtc, read.RefreshedUtc);
    }

    [Fact]
    public async Task APassTheNameServersKeptSilentOnTellsWhy()
    {
        using var bench = new Bench();
        var template = await StaleAsync(bench, TimeSpan.FromDays(2), "domain:example.com");

        var pass = await Refresher(bench, Closed()).RenewAsync(template, CancellationToken.None);

        Assert.NotNull(pass.Silence);
        Assert.Same(template, pass.Template);
    }

    [Fact]
    public async Task NetworksAreWorkedOutWithoutTheNameServers()
    {
        using var bench = new Bench();
        var template = await StaleAsync(bench, TimeSpan.FromDays(2), "10.0.0.0/8");

        var pass = await Refresher(bench, Closed()).RenewAsync(template, CancellationToken.None);

        Assert.Null(pass.Silence);
        Assert.Equal(["10.0.0.0/8"], pass.Template.AllowedIps);
        Assert.Equal(bench.Clock.Now, pass.Template.RefreshedUtc);
    }

    // Adds a template whose ranges were worked out the given time ago and hold an address no entry gives now.
    private static async Task<ClientTemplate> StaleAsync(Bench bench, TimeSpan age, params string[] entries)
    {
        var added = await bench.Templates.AddAsync(
            new ClientTemplate
            {
                Name = "blocked",
                Entries = entries,
                AllowedIps = ["192.0.2.9/32"],
                RefreshedUtc = bench.Clock.Now - age,
            },
            CancellationToken.None);
        Assert.True(added.IsOk, added.Message);

        return added.Record!;
    }

    private static TemplateRenewer Renewer(Bench bench, IPEndPoint server) =>
        new(bench.Templates, Refresher(bench, server), bench.Clock, NullLogger<TemplateRenewer>.Instance);

    private static TemplateRefresher Refresher(Bench bench, IPEndPoint server)
    {
        var resolver = new DnsState();
        resolver.Took(DnsDefaults.Settings with { Upstreams = [$"{server.Address}:{server.Port}"] });

        return new TemplateRefresher(
            bench.Geo,
            bench.GeoFiles,
            bench.Resolver,
            resolver,
            bench.Outbounds,
            bench.Balancers,
            new BalanceLive(),
            bench.Clock);
    }

    // Returns a port of the loopback nobody listens on.
    private static IPEndPoint Closed()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        return (IPEndPoint)socket.Client.LocalEndPoint!;
    }

    // Answers the questions about the names it holds on a port of the loopback.
    private sealed class NameServer : IDisposable
    {
        private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));

        private readonly CancellationTokenSource _stop = new();

        private readonly Dictionary<string, IPAddress> _book;

        public NameServer(params (string Name, string Address)[] book)
        {
            _book = book.ToDictionary(
                one => one.Name,
                one => IPAddress.Parse(one.Address),
                StringComparer.OrdinalIgnoreCase);
            _ = RunAsync();
        }

        public IPEndPoint Point => (IPEndPoint)_socket.Client.LocalEndPoint!;

        public void Dispose()
        {
            _stop.Cancel();
            _socket.Dispose();
            _stop.Dispose();
        }

        private async Task RunAsync()
        {
            try
            {
                while (true)
                {
                    var got = await _socket.ReceiveAsync(_stop.Token);
                    var asked = DnsMessage.Read(got.Buffer)!;
                    var family = asked.Type == DnsRecordType.A ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;
                    var fitting = _book.TryGetValue(asked.Question, out var address) && address.AddressFamily == family
                        ? new[] { address }
                        : [];
                    await _socket.SendAsync(Answer(got.Buffer, asked.Type, fitting), got.RemoteEndPoint, _stop.Token);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }
        }

        private static byte[] Answer(byte[] question, DnsRecordType type, IPAddress[] addresses)
        {
            var packet = new List<byte>(question);
            packet[2] = 0x81;
            packet[3] = 0x80;
            packet[6] = (byte)(addresses.Length >> 8);
            packet[7] = (byte)addresses.Length;
            foreach (var address in addresses)
            {
                var data = address.GetAddressBytes();
                packet.AddRange([0xC0, 0x0C, 0, (byte)type, 0, 1, 0, 0, 0x0E, 0x10, 0, (byte)data.Length]);
                packet.AddRange(data);
            }

            return [.. packet];
        }
    }
}
