using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

public class PanelStartTests
{
    [Fact]
    public void AnEnvironmentThatNamesNothingStartsThePanelOnTheLoopbackUnderAFreshPath()
    {
        var fresh = PanelStart.Of(new Dictionary<string, string> { ["HOME"] = "/root" });

        Assert.Equal(["127.0.0.1"], fresh.Listen);
        Assert.Equal(8443, fresh.Port);
        Assert.Matches("^sub/[a-z0-9]{16}$", fresh.Path);
    }

    [Fact]
    public void TheWebVariablesNameWhereThePanelStarts()
    {
        var fresh = PanelStart.Of(new Dictionary<string, string> { ["Web__Listen__0"] = "*:9443", ["Web__Path"] = "/" });

        Assert.Empty(fresh.Listen);
        Assert.Equal(9443, fresh.Port);
        Assert.Equal("/", fresh.Prefix);
    }

    [Fact]
    public void TheListenVariablesAreTakenInTheOrderOfTheirNumbers()
    {
        var fresh = PanelStart.Of(new Dictionary<string, string>
        {
            ["Web__Listen__1"] = "[::1]:9443",
            ["WEB__LISTEN__0"] = "127.0.0.1:9443",
            ["Web__Listen__x"] = "*:8443",
        });

        Assert.Equal(["127.0.0.1", "::1"], fresh.Listen);
        Assert.Equal(9443, fresh.Port);
    }

    [Fact]
    public void TheEnvironmentStartsThePanelWhereTheConfigurationDoes()
    {
        var named = PanelStart.Of(new Dictionary<string, string>
        {
            ["Web__Listen__0"] = "127.0.0.1:5080",
            ["Web__Listen__1"] = "[::1]:5080",
            ["Web__Path"] = "panel",
        });
        var configured = Listening.Draft(new WebOptions { Listen = ["127.0.0.1:5080", "[::1]:5080"], Path = "panel" });

        Assert.Equal(configured.Listen, named.Listen);
        Assert.Equal(configured.Port, named.Port);
        Assert.Equal(configured.Prefix, named.Prefix);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData("/panel/")]
    public void APathTheEnvironmentNamesPassesTheRules(string path)
    {
        var fresh = PanelStart.Of(new Dictionary<string, string> { ["Web__Listen__0"] = "127.0.0.1:8443", ["Web__Path"] = path });

        Assert.Null(PanelRules.Check(fresh));
    }

    [Fact]
    public void TheRootTheEnvironmentNamesIsWrittenAsACommandWritesIt()
    {
        var fresh = PanelStart.Of(new Dictionary<string, string> { ["Web__Path"] = "/" });

        Assert.Equal(PanelEdit.PathOf("/"), fresh.Path);
        Assert.Equal("/", fresh.Prefix);
    }

    [Fact]
    public async Task ARootWrittenWithASlashTakesAChangeOfTheOtherSettings()
    {
        using var bench = new Bench();
        await bench.Panel.SeedAsync(PanelDefaults.Settings with { Path = "/" }, CancellationToken.None);

        var held = await bench.Panel.ReadAsync(CancellationToken.None);
        var saved = await bench.Panel.SaveAsync(held with { Port = 9443 }, CancellationToken.None);

        Assert.Empty(held.Path);
        Assert.True(saved.IsOk);
        Assert.Equal("/", PanelStore.Held(bench.DatabasePath)?.Prefix);
    }

    [Fact]
    public async Task APanelThatHoldsSettingsMakesNoFreshOnes()
    {
        using var bench = new Bench();
        await bench.Panel.SeedAsync(PanelDefaults.Settings with { Port = 5080 }, CancellationToken.None);

        var held = await bench.Panel.SeedAsync(() => throw new InvalidOperationException("made"), CancellationToken.None);

        Assert.Equal(5080, held.Port);
    }
}
