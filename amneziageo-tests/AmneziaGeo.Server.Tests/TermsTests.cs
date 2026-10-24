using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

public class TermsTests
{
    [Fact]
    public async Task ANewPanelHoldsNoAcceptanceAndAsksForTheTerms()
    {
        using var bench = new Bench();
        var store = new TermsStore(bench.Db, bench.Clock);

        var latest = await store.LatestAsync(CancellationToken.None);
        var state = TermsEndpoints.State(latest);

        Assert.Null(latest);
        Assert.Equal(TermsEndpoints.Version, state.Version);
        Assert.False(state.Accepted);
        Assert.Null(state.Last);
    }

    [Fact]
    public async Task AnAcceptanceIsKeptWithTheAccountAndTheTime()
    {
        using var bench = new Bench();
        await new TermsStore(bench.Db, bench.Clock).AcceptAsync(TermsEndpoints.Version, 7, "admin", CancellationToken.None);

        var latest = await new TermsStore(bench.Db, bench.Clock).LatestAsync(CancellationToken.None);

        Assert.Equal(new TermsAcceptance(TermsEndpoints.Version, "admin", bench.Clock.Now), latest);
        Assert.True(TermsEndpoints.State(latest).Accepted);
    }

    [Fact]
    public async Task TheLatestAcceptanceStandsForThePanel()
    {
        using var bench = new Bench();
        var store = new TermsStore(bench.Db, bench.Clock);
        await store.AcceptAsync(TermsEndpoints.Version, 1, "first", CancellationToken.None);
        bench.Clock.Pass(TimeSpan.FromDays(1));
        await store.AcceptAsync(TermsEndpoints.Version, 2, "second", CancellationToken.None);

        var latest = await store.LatestAsync(CancellationToken.None);

        Assert.Equal("second", latest!.Login);
        Assert.Equal(bench.Clock.Now, latest.AcceptedUtc);
    }

    [Fact]
    public void TermsOfAnOlderVersionAreAskedAgain()
    {
        var state = TermsEndpoints.State(new TermsAcceptance(TermsEndpoints.Version - 1, "admin", DateTimeOffset.UnixEpoch));

        Assert.False(state.Accepted);
        Assert.Equal(TermsEndpoints.Version - 1, state.Last!.Version);
    }

    [Fact]
    public async Task ALongLoginIsCutToItsColumn()
    {
        using var bench = new Bench();

        var accepted = await new TermsStore(bench.Db, bench.Clock).AcceptAsync(TermsEndpoints.Version, null, new string('a', 300), CancellationToken.None);

        Assert.Equal(256, accepted.Login.Length);
    }

    [Theory]
    [InlineData("TERMS.md", "Version ")]
    [InlineData("TERMS.ru.md", "Версия ")]
    [InlineData("PRIVACY.md", "Version ")]
    [InlineData("PRIVACY.ru.md", "Версия ")]
    public void TheTextsNameTheVersionThePanelAsksFor(string file, string word)
    {
        var line = File.ReadLines(Path.Combine(Root(new DirectoryInfo(AppContext.BaseDirectory)), file))
            .Select(one => one.Trim())
            .First(one => one.StartsWith(word, StringComparison.Ordinal));

        Assert.StartsWith(word + TermsEndpoints.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + (word == "Version " ? "," : " "), line);
    }

    // The root of the repository, found upwards from the tests.
    private static string Root(DirectoryInfo? folder) => folder is null
        ? throw new DirectoryNotFoundException("the root of the repository")
        : File.Exists(Path.Combine(folder.FullName, "AmneziaGeo.Server.slnx")) ? folder.FullName : Root(folder.Parent);
}
