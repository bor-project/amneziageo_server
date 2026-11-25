using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

public class DnsSettingsTests
{
    [Fact]
    public void SettingsAreComparedByWhatTheyHold()
    {
        var running = new DnsSettings { Listen = ["10.8.0.1"] };
        var saved = running with { Listen = ["10.8.0.1"] };

        Assert.False(saved.Differs(running));
    }

    [Fact]
    public void AnyOtherValueDiffers()
    {
        var running = new DnsSettings();

        Assert.True((running with { Port = 5300 }).Differs(running));
        Assert.True((running with { CacheSize = running.CacheSize + 1 }).Differs(running));
        Assert.True((running with { MaxTtl = running.MaxTtl + 1 }).Differs(running));
        Assert.True((running with { IsEnabled = !running.IsEnabled }).Differs(running));
    }

    [Fact]
    public void TheStateKeepsTheSettingsTheResolverTook()
    {
        var state = new DnsState();
        var settings = new DnsSettings { IsEnabled = true };

        Assert.Null(state.Settings);

        state.Took(settings);

        Assert.Same(settings, state.Settings);
    }
}
