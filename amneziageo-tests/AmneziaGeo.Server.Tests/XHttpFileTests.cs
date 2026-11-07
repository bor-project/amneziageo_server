using System.Text.Json;
using AmneziaGeo.Server.Routing.XHttp;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class XHttpFileTests
{
    private const string FirstUser = "a975342c-25a1-4148-b0b5-7223bf6acd2d";
    private const string SecondUser = "694e2645-a27f-448a-b550-58afc5f4a4ab";

    [Fact]
    public void RendersGetWithoutBodyAndALoopbackHttpListener()
    {
        using var document = JsonDocument.Parse(XHttpFile.Render(new XHttpProxy(7, 18080, "/tunnel/", [FirstUser])));
        var inbound = document.RootElement.GetProperty("inbounds")[0];
        Assert.Equal("127.0.0.1", inbound.GetProperty("listen").GetString());
        Assert.Equal(18080, inbound.GetProperty("port").GetInt32());
        Assert.Equal("vless", inbound.GetProperty("protocol").GetString());
        Assert.Equal("none", inbound.GetProperty("settings").GetProperty("decryption").GetString());
        var stream = inbound.GetProperty("streamSettings");
        Assert.Equal("none", stream.GetProperty("security").GetString());
        Assert.Equal("xhttp", stream.GetProperty("network").GetString());
        Assert.False(stream.TryGetProperty("tlsSettings", out _));
        var xhttp = stream.GetProperty("xhttpSettings");
        Assert.Equal("/tunnel/", xhttp.GetProperty("path").GetString());
        Assert.Equal("packet-up", xhttp.GetProperty("mode").GetString());
        Assert.False(xhttp.TryGetProperty("host", out _));
        var extra = xhttp.GetProperty("extra");
        Assert.Equal("GET", extra.GetProperty("uplinkHTTPMethod").GetString());
        Assert.Equal("header", extra.GetProperty("uplinkDataPlacement").GetString());
        Assert.Equal(2048, extra.GetProperty("scMaxEachPostBytes").GetInt32());
        Assert.Equal(2048, extra.GetProperty("uplinkChunkSize").GetInt32());
        Assert.Equal(8192, extra.GetProperty("serverMaxHeaderBytes").GetInt32());
        Assert.Equal("100-100", extra.GetProperty("xPaddingBytes").GetString());
        Assert.False(extra.TryGetProperty("noSSEHeader", out _));
    }

    [Fact]
    public void NormalizesAndDeduplicatesUsersWithoutChangingTheConfigurationOrder()
    {
        var first = XHttpFile.Render(new XHttpProxy(1, 18080, "tunnel", [FirstUser.ToUpperInvariant(), SecondUser, FirstUser]));
        var second = XHttpFile.Render(new XHttpProxy(1, 18080, "/tunnel/", [SecondUser, Guid.Parse(FirstUser).ToString("N")]));
        Assert.Equal(first, second);
        using var document = JsonDocument.Parse(first);
        var clients = document.RootElement.GetProperty("inbounds")[0].GetProperty("settings").GetProperty("clients");
        Assert.Equal(2, clients.GetArrayLength());
        Assert.Equal(SecondUser, clients[0].GetProperty("id").GetString());
        Assert.Equal(FirstUser, clients[1].GetProperty("id").GetString());
    }

    [Fact]
    public void AllowsUdpToLocalAndRemoteTargetsAndBlocksOtherNetworks()
    {
        using var document = JsonDocument.Parse(XHttpFile.Render(new XHttpProxy(1, 18080, "tunnel", [FirstUser])));
        var outbound = document.RootElement.GetProperty("outbounds")[0];
        Assert.Equal("freedom", outbound.GetProperty("protocol").GetString());
        Assert.False(outbound.GetProperty("settings").TryGetProperty("redirect", out _));
        var rules = outbound.GetProperty("settings").GetProperty("finalRules");
        Assert.Equal("allow", rules[0].GetProperty("action").GetString());
        Assert.Equal("udp", rules[0].GetProperty("network").GetString());
        Assert.False(rules[0].TryGetProperty("ip", out _));
        Assert.False(rules[0].TryGetProperty("port", out _));
        Assert.Equal("block", rules[1].GetProperty("action").GetString());
    }

    [Theory]
    [InlineData(0, 18080)]
    [InlineData(-1, 18080)]
    [InlineData(1, 0)]
    [InlineData(1, 65536)]
    public void RejectsInvalidIdentityAndPort(long id, int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => XHttpFile.Render(new XHttpProxy(id, port, "tunnel", [FirstUser])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("first/second")]
    [InlineData("tunnel?query=1")]
    [InlineData("tunnel#fragment")]
    [InlineData("%2fprivate")]
    [InlineData("tunnel\r\nX-Data: value")]
    public void RejectsPathsThatDoNotNameOneUrlSegment(string path)
    {
        Assert.Throws<ArgumentException>(() => XHttpFile.Render(new XHttpProxy(1, 18080, path, [FirstUser])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void RejectsInvalidUsersWithoutEchoingTheirValue(string credential)
    {
        var error = Assert.Throws<ArgumentException>(() => XHttpFile.Render(new XHttpProxy(1, 18080, "tunnel", [credential])));
        if (credential.Length > 0)
        {
            Assert.DoesNotContain(credential, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EmptyUsersProduceAPerListenerFaultWithoutStartingTheExecutable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "xhttp-" + Guid.NewGuid().ToString("N"));
        await using var host = new XHttpHost(directory, Path.Combine(directory, "missing-xray"), NullLogger<XHttpHost>.Instance);
        var faults = await host.ApplyAsync([new XHttpProxy(7, 18080, "tunnel", [])], default).ConfigureAwait(false);
        Assert.Contains(7L, faults.Keys);
        Assert.Contains("at least one user", faults[7], StringComparison.Ordinal);
        Assert.False(host.State(7).IsRunning);
        Assert.Equal(faults[7], host.State(7).Fault);
        Assert.False(Directory.Exists(directory));
        Assert.Empty(await host.ApplyAsync([], default).ConfigureAwait(false));
    }

    [Fact]
    public async Task OneInvalidListenerDoesNotPreventProcessingTheOtherListeners()
    {
        var directory = Path.Combine(Path.GetTempPath(), "xhttp-" + Guid.NewGuid().ToString("N"));
        await using var host = new XHttpHost(directory, Path.Combine(directory, "missing-xray"), NullLogger<XHttpHost>.Instance);
        var faults = await host.ApplyAsync(
            [new XHttpProxy(1, 18080, "one", []), new XHttpProxy(2, 18081, "two", [FirstUser])], default).ConfigureAwait(false);
        Assert.Equal(2, faults.Count);
        Assert.Contains("at least one user", faults[1], StringComparison.Ordinal);
        Assert.Contains("did not start", faults[2], StringComparison.Ordinal);
        Assert.DoesNotContain(FirstUser, faults[2], StringComparison.OrdinalIgnoreCase);
    }
}
