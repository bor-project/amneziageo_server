using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

public class DnsSettingsTests
{
    [Fact]
    public void SettingsAreComparedByWhatTheyHold()
    {
        var running = new DnsSettings { Upstreams = ["1.1.1.1"], Listen = ["10.8.0.1"] };
        var saved = running with { Upstreams = ["1.1.1.1"], Listen = ["10.8.0.1"] };

        Assert.False(saved.Differs(running));
    }

    [Fact]
    public void AnyOtherValueDiffers()
    {
        var running = new DnsSettings();

        Assert.True((running with { Port = 5300 }).Differs(running));
        Assert.True((running with { Upstreams = ["9.9.9.9", "149.112.112.112"] }).Differs(running));
        Assert.True((running with { BlockDoh = !running.BlockDoh }).Differs(running));
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
