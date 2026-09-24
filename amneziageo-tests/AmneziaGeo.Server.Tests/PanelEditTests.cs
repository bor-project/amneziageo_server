using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Tests;

public class PanelEditTests
{
    [Fact]
    public void AResetPutsThePanelOnTheLoopbackUnderAFreshPath()
    {
        var held = PanelDefaults.Settings with
        {
            Listen = [],
            Domains = ["panel.example"],
            Port = 9443,
            Path = "panel",
            Opened = true,
            Certificate = "/etc/letsencrypt/live/panel.example/fullchain.pem",
            CertificateKey = "/etc/letsencrypt/live/panel.example/privkey.pem",
            Language = "ru",
            Prereleases = true,
            NameTemplate = "{CLIENT}",
        };

        var reset = PanelEdit.Reset(held);

        Assert.Equal([PanelEdit.Loopback], reset.Listen);
        Assert.Empty(reset.Domains);
        Assert.Equal(PanelDefaults.Port, reset.Port);
        Assert.Matches("^sub/[a-z0-9]{16}$", reset.Path);
        Assert.Equal(string.Empty, reset.Certificate);
        Assert.Equal(string.Empty, reset.CertificateKey);
        Assert.True(reset.Opened);
        Assert.Equal("ru", reset.Language);
        Assert.True(reset.Prereleases);
        Assert.Equal("{CLIENT}", reset.NameTemplate);
        Assert.Null(PanelRules.Check(reset));
    }

    [Theory]
    [InlineData("/", "")]
    [InlineData("/panel/", "panel")]
    [InlineData("sub/one", "sub/one")]
    public void APathIsTakenAsItIsWrittenWithoutItsSlashes(string word, string path)
    {
        Assert.Equal(path, PanelEdit.PathOf(word));
    }

    [Fact]
    public void ARandomPathIsOneNoOneGuesses()
    {
        Assert.Matches("^sub/[a-z0-9]{16}$", PanelEdit.PathOf(PanelEdit.Random));
        Assert.NotEqual(PanelEdit.PathOf(PanelEdit.Random), PanelEdit.PathOf(PanelEdit.Random));
    }

    [Fact]
    public void AListTakesCommasAndSemicolonsAndTheWordForNothing()
    {
        Assert.Equal(["10.0.0.1", "10.0.0.2"], PanelEdit.ListOf("10.0.0.1, 10.0.0.2", PanelEdit.All));
        Assert.Equal(["a.example", "b.example"], PanelEdit.ListOf("a.example;b.example", PanelEdit.None));
        Assert.Empty(PanelEdit.ListOf("all", PanelEdit.All));
        Assert.Empty(PanelEdit.ListOf("NONE", PanelEdit.None));
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("Off", false)]
    [InlineData("maybe", null)]
    public void ASwitchIsReadFromItsWord(string word, bool? expected)
    {
        Assert.Equal(expected, PanelEdit.SwitchOf(word));
    }

    [Fact]
    public void TheChannelIsReadBothWays()
    {
        Assert.True(PanelEdit.ChannelOf("test"));
        Assert.False(PanelEdit.ChannelOf("stable"));
        Assert.Null(PanelEdit.ChannelOf("nightly"));
        Assert.Equal(PanelEdit.Test, PanelEdit.Channel(PanelDefaults.Settings with { Prereleases = true }));
        Assert.Equal(PanelEdit.Stable, PanelEdit.Channel(PanelDefaults.Settings));
    }
}
