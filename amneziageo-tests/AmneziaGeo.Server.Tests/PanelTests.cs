using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

public class PanelTests
{
    [Fact]
    public void ThePanelStartsOnEveryAddressOfItsOwnPortAtTheRoot()
    {
        var settings = PanelDefaults.Settings;

        Assert.Equal(8443, settings.Port);
        Assert.Equal(["*:8443"], settings.Entries);
        Assert.Equal("/", settings.Prefix);
        Assert.Equal("auto", settings.Language);
    }

    [Fact]
    public void EveryAddressOfTheSettingsIsBoundOnTheSamePort()
    {
        var settings = PanelDefaults.Settings with { Listen = ["127.0.0.1", "::1"], Port = 9443 };

        Assert.Equal(["127.0.0.1:9443", "[::1]:9443"], settings.Entries);
        Assert.Equal(2, Listening.Plan(settings.Entries).Points.Count);
    }

    [Theory]
    [InlineData("panel", "/panel/")]
    [InlineData("/panel", "/panel/")]
    [InlineData("/panel/deep/", "/panel/deep/")]
    public void ThePathIsBoundedBySlashes(string path, string prefix)
    {
        Assert.Equal(prefix, (PanelDefaults.Settings with { Path = path }).Prefix);
    }

    [Theory]
    [InlineData(0, "bad-port")]
    [InlineData(70000, "bad-port")]
    public void APortOutsideTheRangeIsRefused(int port, string code)
    {
        Assert.Equal(code, PanelRules.Check(PanelDefaults.Settings with { Port = port })?.Code);
    }

    [Fact]
    public void AnAddressThatIsNotOneIsRefused()
    {
        var settings = PanelDefaults.Settings with { Listen = ["127.0.0.1", "ens3"] };

        Assert.Equal("bad-listen", PanelRules.Check(settings)?.Code);
    }

    [Theory]
    [InlineData("panel .ru")]
    [InlineData(".panel.ru")]
    [InlineData("panel..ru")]
    public void ADomainThatIsNotOneIsRefused(string domain)
    {
        Assert.Equal("bad-domain", PanelRules.Check(PanelDefaults.Settings with { Domains = [domain] })?.Code);
    }

    [Theory]
    [InlineData("panel page")]
    [InlineData("../etc")]
    [InlineData("//")]
    public void APathThatIsNotOneIsRefused(string path)
    {
        Assert.Equal("bad-path", PanelRules.Check(PanelDefaults.Settings with { Path = path })?.Code);
    }

    [Fact]
    public void ALanguageThePanelDoesNotOpenInIsRefused()
    {
        Assert.Equal("bad-language", PanelRules.Check(PanelDefaults.Settings with { Language = "de" })?.Code);
    }

    [Theory]
    [InlineData("/srv/chain.pem", "")]
    [InlineData("", "/srv/key.pem")]
    [InlineData("srv/chain.pem", "srv/key.pem")]
    public void ACertificateThatIsNotAPairOfPathsIsRefused(string chain, string key)
    {
        var settings = PanelDefaults.Settings with { Certificate = chain, CertificateKey = key };

        Assert.Equal("bad-certificate", PanelRules.Check(settings)?.Code);
    }

    [Fact]
    public void ALineOfNamesLosesTheBlanksAndTheRepeats()
    {
        Assert.Equal(["panel.example", "vpn.example"], PanelList.Split(" panel.example; vpn.example ;;PANEL.example"));
        Assert.Empty(PanelList.Split(null));
    }

    [Fact]
    public void NamesInARequestArriveAsAList()
    {
        var asked = PanelAnswers.Draft(
            new PanelRequest(["127.0.0.1"], ["panel.example; vpn.example"], 8443, "/panel/", null, null, "ru"));

        Assert.Equal(["127.0.0.1"], asked.Listen);
        Assert.Equal(["panel.example", "vpn.example"], asked.Domains);
        Assert.Equal("panel", asked.Path);
    }

    [Fact]
    public void ThePathIsReadBackBoundedBySlashes()
    {
        var settings = PanelDefaults.Settings with { Path = "panel" };

        var answer = PanelAnswers.Panel(settings, settings, new WebOptions());

        Assert.Equal("/panel/", answer.Path);
        Assert.False(answer.Pending);
    }

    [Fact]
    public void AnotherPortWaitsForARestart()
    {
        var running = PanelDefaults.Settings;

        var answer = PanelAnswers.Panel(running with { Port = 9443 }, running, new WebOptions());

        Assert.True(answer.Pending);
    }

    [Fact]
    public void TheLanguageTakesNoRestart()
    {
        var running = PanelDefaults.Settings with { Language = "en" };

        Assert.False((running with { Language = "ru" }).Differs(running));
    }

    [Fact]
    public void SettingsAreComparedByWhatTheyHold()
    {
        var running = PanelDefaults.Settings with { Listen = ["127.0.0.1"], Domains = ["panel.example"], Path = "panel" };
        var saved = running with { Listen = ["127.0.0.1"], Domains = ["PANEL.example"], Path = "/panel/" };

        Assert.False(saved.Differs(running));
        Assert.True((saved with { Listen = ["10.8.0.1"] }).Differs(running));
    }

    [Fact]
    public void SettingsTheRulesAllowArePassed()
    {
        var settings = new PanelSettings
        {
            Listen = ["127.0.0.1", "10.8.0.1"],
            Domains = ["myvpn-ru.ddns.net", "panel.example"],
            Port = 8443,
            Path = "panel",
            Certificate = "/etc/letsencrypt/live/myvpn-ru.ddns.net/fullchain.pem",
            CertificateKey = "/etc/letsencrypt/live/myvpn-ru.ddns.net/privkey.pem",
            Language = "ru",
        };

        Assert.Null(PanelRules.Check(settings));
    }

    [Fact]
    public void TheCertificateOfThePanelWinsOverTheConfiguration()
    {
        var options = new WebOptions { Certificate = "/srv/chain.pem", CertificateKey = "/srv/key.pem" };
        var settings = PanelDefaults.Settings with
        {
            Certificate = "/etc/letsencrypt/live/myvpn-ru.ddns.net/fullchain.pem",
            CertificateKey = "/etc/letsencrypt/live/myvpn-ru.ddns.net/privkey.pem",
        };

        Assert.Equal("/etc/letsencrypt/live/myvpn-ru.ddns.net/fullchain.pem", Listening.Chain(options, settings));
        Assert.Equal("/etc/letsencrypt/live/myvpn-ru.ddns.net/privkey.pem", Listening.Key(options, settings));
    }

    [Fact]
    public void WithoutACertificateThePanelTakesTheOneTheConfigurationNames()
    {
        var options = new WebOptions { Certificate = "/srv/chain.pem", CertificateKey = "/srv/key.pem" };

        Assert.Equal("/srv/chain.pem", Listening.Chain(options, PanelDefaults.Settings));
        Assert.Equal("/srv/key.pem", Listening.Key(options, PanelDefaults.Settings));
    }

    [Fact]
    public void TheListenListOfTheConfigurationBecomesTheFirstSettings()
    {
        var settings = Listening.Draft(new WebOptions { Listen = ["127.0.0.1:5080", "[::1]:5080"] });

        Assert.Equal(["127.0.0.1", "::1"], settings.Listen);
        Assert.Equal(5080, settings.Port);
        Assert.Empty(Listening.Draft(new WebOptions { Listen = ["*:5080"] }).Listen);
    }

    [Fact]
    public void ThePageCarriesThePathThePanelSitsUnder()
    {
        const string page = "<!doctype html><html><head><base href=\"/\" /><title>a</title></head></html>";

        Assert.Contains("<base href=\"/panel/\" />", PanelIndex.Under(page, "/panel/"), StringComparison.Ordinal);
    }

    [Fact]
    public void APageWithoutABaseGetsOne()
    {
        const string page = "<!doctype html><html><head><title>a</title></head></html>";

        Assert.Contains("<head><base href=\"/panel/\" />", PanelIndex.Under(page, "/panel/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFreshInstallCarriesTheSettingsThePanelStartsWith()
    {
        using var bench = new Bench();

        var held = await bench.Panel.ReadAsync(CancellationToken.None);

        Assert.Equal(PanelDefaults.Port, held.Port);
        Assert.Empty(held.Listen);
        Assert.Empty(held.Path);
    }

    [Fact]
    public async Task SettingsAreReadBackAsTheyWereSaved()
    {
        using var bench = new Bench();
        var settings = new PanelSettings
        {
            Listen = ["127.0.0.1", "10.8.0.1"],
            Domains = ["panel.example", "vpn.example"],
            Port = 9443,
            Path = "panel",
            Certificate = "/srv/panel/chain.pem",
            CertificateKey = "/srv/panel/key.pem",
            Language = "ru",
        };

        var saved = await bench.Panel.SaveAsync(settings, CancellationToken.None);
        var held = await bench.Panel.ReadAsync(CancellationToken.None);

        Assert.True(saved.IsOk);
        Assert.Equal(settings.Listen, held.Listen);
        Assert.Equal(settings.Domains, held.Domains);
        Assert.Equal(settings.Port, held.Port);
        Assert.Equal(settings.Path, held.Path);
        Assert.Equal(settings.Certificate, held.Certificate);
        Assert.Equal(settings.CertificateKey, held.CertificateKey);
        Assert.Equal(settings.Language, held.Language);
    }

    [Fact]
    public async Task SettingsTheRulesRefuseAreNotSaved()
    {
        using var bench = new Bench();

        var refused = await bench.Panel.SaveAsync(PanelDefaults.Settings with { Port = 0 }, CancellationToken.None);
        var held = await bench.Panel.ReadAsync(CancellationToken.None);

        Assert.False(refused.IsOk);
        Assert.Equal("bad-port", refused.Code);
        Assert.Equal(PanelDefaults.Port, held.Port);
    }

    [Fact]
    public async Task WhatWasSeededOnceIsLeftAlone()
    {
        using var bench = new Bench();

        await bench.Panel.SeedAsync(PanelDefaults.Settings with { Port = 5080 }, CancellationToken.None);
        var again = await bench.Panel.SeedAsync(PanelDefaults.Settings with { Port = 9443 }, CancellationToken.None);

        Assert.Equal(5080, again.Port);
    }

    [Fact]
    public async Task TheServerReadsTheSettingsBeforeItIsBuilt()
    {
        using var bench = new Bench();
        await bench.Panel.SaveAsync(
            PanelDefaults.Settings with { Port = 9443, Listen = ["127.0.0.1", "::1"], Language = "ru" },
            CancellationToken.None);

        var held = PanelStore.Held(bench.DatabasePath);

        Assert.NotNull(held);
        Assert.Equal(["127.0.0.1:9443", "[::1]:9443"], held.Entries);
        Assert.Equal("ru", held.Language);
    }

    [Fact]
    public void ADatabaseThatIsNotThereLeavesTheSettingsToTheConfiguration()
    {
        Assert.Null(PanelStore.Held(Path.Combine(Path.GetTempPath(), $"amneziageo-{Guid.NewGuid():N}.db")));
    }
}
