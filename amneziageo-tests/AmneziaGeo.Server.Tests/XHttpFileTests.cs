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
    public void RendersGetListenerWithoutLegacyRuntimeSettings()
    {
        using var document = JsonDocument.Parse(XHttpFile.Render(new XHttpProxy(7, 18080, "/tunnel/", [FirstUser])));
        var root = document.RootElement;
        Assert.Single(root.EnumerateObject());
        var listener = root.GetProperty("httpGet");
        Assert.Equal(2, listener.GetProperty("version").GetInt32());
        Assert.Equal(18080, listener.GetProperty("port").GetInt32());
        Assert.Equal("/tunnel/", listener.GetProperty("path").GetString());
        Assert.Equal(FirstUser, Assert.Single(listener.GetProperty("credentials").EnumerateArray()).GetString());
        Assert.False(listener.GetProperty("restricted").GetBoolean());
        Assert.Empty(listener.GetProperty("destinations").EnumerateArray());
    }

    [Fact]
    public void NormalizesAndDeduplicatesUsersWithoutChangingTheConfigurationOrder()
    {
        var first = XHttpFile.Render(new XHttpProxy(1, 18080, "tunnel", [FirstUser.ToUpperInvariant(), SecondUser, FirstUser]));
        var second = XHttpFile.Render(new XHttpProxy(1, 18080, "/tunnel/", [SecondUser, Guid.Parse(FirstUser).ToString("N")]));
        Assert.Equal(first, second);
        using var document = JsonDocument.Parse(first);
        var clients = document.RootElement.GetProperty("httpGet").GetProperty("credentials");
        Assert.Equal(2, clients.GetArrayLength());
        Assert.Equal(SecondUser, clients[0].GetString());
        Assert.Equal(FirstUser, clients[1].GetString());
    }

    [Fact]
    public void PreservesRestrictedDestinationPolicy()
    {
        var policy = new AmneziaGeo.Server.Core.Proxy.ProxyTargets(true,
            [new AmneziaGeo.Server.Core.Proxy.ProxyDestination("127.0.0.1", 51820)]);
        using var document = JsonDocument.Parse(XHttpFile.Render(new XHttpProxy(1, 18080, "tunnel", [FirstUser], policy)));
        var listener = document.RootElement.GetProperty("httpGet");
        Assert.True(listener.GetProperty("restricted").GetBoolean());
        var target = Assert.Single(listener.GetProperty("destinations").EnumerateArray());
        Assert.Equal("127.0.0.1", target.GetProperty("host").GetString());
        Assert.Equal(51820, target.GetProperty("port").GetInt32());
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
        await using var host = new XHttpHost(directory, Path.Combine(directory, "missing-get"), NullLogger<XHttpHost>.Instance);
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
        await using var host = new XHttpHost(directory, Path.Combine(directory, "missing-get"), NullLogger<XHttpHost>.Instance);
        var faults = await host.ApplyAsync(
            [new XHttpProxy(1, 18080, "one", []), new XHttpProxy(2, 18081, "two", [FirstUser])], default).ConfigureAwait(false);
        Assert.Equal(2, faults.Count);
        Assert.Contains("at least one user", faults[1], StringComparison.Ordinal);
        Assert.Contains("did not start", faults[2], StringComparison.Ordinal);
        Assert.DoesNotContain(FirstUser, faults[2], StringComparison.OrdinalIgnoreCase);
    }
}
