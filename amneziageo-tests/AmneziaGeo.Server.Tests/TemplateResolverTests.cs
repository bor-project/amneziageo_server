using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Tests;

public class TemplateResolverTests
{
    [Fact]
    public async Task NetworksStayAndNamesTurnIntoTheirAddresses()
    {
        var book = new NameBook(new() { ["example.com"] = ["93.184.216.34", "2606:2800:220:1:248:1893:25c8:1946"] });

        var found = await new TemplateResolver(book).ResolveAsync(
            ["10.0.0.0/8", "1.2.3.4", "example.com"],
            Index(),
            CancellationToken.None);

        Assert.Equal(
            ["1.2.3.4/32", "10.0.0.0/8", "93.184.216.34/32", "2606:2800:220:1:248:1893:25c8:1946/128"],
            found.AllowedIps);
        Assert.Empty(found.Missed);
    }

    [Fact]
    public async Task AGeoKeyGivesItsRangesAndTheAddressesOfItsNames()
    {
        var book = new NameBook(new() { ["yandex.ru"] = ["77.88.8.8"], ["ya.ru"] = ["87.250.250.242"] });

        var found = await new TemplateResolver(book).ResolveAsync(
            ["geoip:ru", "geosite:yandex"],
            Index(),
            CancellationToken.None);

        Assert.Equal(["77.88.8.0/24", "87.250.250.242/32"], found.AllowedIps);
        Assert.Empty(found.Missed);
    }

    [Fact]
    public async Task AnEntryThatGivesNothingIsMissed()
    {
        var found = await new TemplateResolver(new NameBook(new())).ResolveAsync(
            ["geoip:zz", "10.0.0.0/8", "geosite:none", "nowhere.example"],
            Index(),
            CancellationToken.None);

        Assert.Equal(["10.0.0.0/8"], found.AllowedIps);
        Assert.Equal(["geoip:zz", "geosite:none", "nowhere.example"], found.Missed);
    }

    [Fact]
    public async Task ANameIsAskedForBothFamiliesOnce()
    {
        var book = new NameBook(new() { ["yandex.ru"] = ["77.88.8.8"] });

        await new TemplateResolver(book).ResolveAsync(["yandex.ru", "geosite:yandex"], Index(), CancellationToken.None);

        Assert.Equal(["ya.ru A", "ya.ru Aaaa", "yandex.ru A", "yandex.ru Aaaa"], book.Asked.Order(StringComparer.Ordinal));
    }

    private static GeoIndex Index()
    {
        var files = new MemoryGeoFiles();
        files.Put("ip", GeoBuilder.Ip(("RU", ["77.88.8.0/24"])));
        files.Put("site", GeoBuilder.Site(("YANDEX", ["yandex.ru", "ya.ru"])));

        return GeoIndex.Load(
            [
                new GeoSource { Name = "ip", Kind = GeoKind.Ip, Position = 1 },
                new GeoSource { Name = "site", Kind = GeoKind.Site, Position = 2 },
            ],
            files);
    }

    private sealed class NameBook(Dictionary<string, string[]> book) : IDnsUpstream
    {
        public List<string> Asked { get; } = [];

        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            var asked = DnsMessage.Read(question.Span)!;
            lock (Asked)
            {
                Asked.Add($"{asked.Question} {asked.Type}");
            }

            if (!book.TryGetValue(asked.Question, out var addresses))
            {
                return Task.FromResult<byte[]?>(null);
            }

            var family = asked.Type == DnsRecordType.A ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;
            var fitting = addresses.Select(IPAddress.Parse).Where(address => address.AddressFamily == family).ToArray();

            return Task.FromResult<byte[]?>(Answer(question.Span, asked.Type, fitting));
        }

        private static byte[] Answer(ReadOnlySpan<byte> question, DnsRecordType type, IPAddress[] addresses)
        {
            var packet = new List<byte>(question.ToArray());
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
