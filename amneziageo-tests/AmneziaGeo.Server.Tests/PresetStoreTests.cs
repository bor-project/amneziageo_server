using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Tests;

public class PresetStoreTests
{
    [Fact]
    public async Task APresetReadsBackAsItWasWritten()
    {
        using var bench = new Bench();

        var added = await bench.Presets.AddAsync(
            Fresh("blocked") with { Direct = ["geoip:RU"], Block = ["https://ads.example.com/x"], AllUdp = true, Full = true },
            CancellationToken.None);
        var read = await bench.Presets.FindAsync(added.Record!.Id, CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        Assert.Equal("blocked", read!.Name);
        Assert.Equal(["geosite:youtube", "cidr:10.0.0.0/8"], read.Proxy);
        Assert.Equal(["geoip:ru"], read.Direct);
        Assert.Equal(["domain:ads.example.com"], read.Block);
        Assert.True(read.AllUdp);
        Assert.True(read.Full);
    }

    [Fact]
    public async Task TwoPresetsUnderOneNameAreRefused()
    {
        using var bench = new Bench();

        await bench.Presets.AddAsync(Fresh("blocked"), CancellationToken.None);
        var second = await bench.Presets.AddAsync(Fresh(" blocked "), CancellationToken.None);

        Assert.Equal(PresetOutcome.NameTaken, second.Outcome);
        Assert.Equal("name-taken", second.Code);
    }

    [Theory]
    [InlineData("keyword:ads", "bad-entry")]
    [InlineData("not an entry", "bad-entry")]
    public async Task ABadEntryIsRefusedByCode(string entry, string code)
    {
        using var bench = new Bench();

        var added = await bench.Presets.AddAsync(Fresh("blocked") with { Block = [entry] }, CancellationToken.None);

        Assert.Equal(PresetOutcome.Invalid, added.Outcome);
        Assert.Equal(code, added.Code);
    }

    [Fact]
    public async Task APresetWithoutANameIsRefused()
    {
        using var bench = new Bench();

        var added = await bench.Presets.AddAsync(Fresh("  "), CancellationToken.None);

        Assert.Equal("bad-preset-name", added.Code);
    }

    [Fact]
    public async Task APresetATemplateNamesIsNotRemoved()
    {
        using var bench = new Bench();
        var preset = (await bench.Presets.AddAsync(Fresh("blocked"), CancellationToken.None)).Record!;
        await bench.Templates.AddAsync(Template("phones") with { Presets = [preset.Id] }, CancellationToken.None);

        var removed = await bench.Presets.RemoveAsync(preset.Id, CancellationToken.None);
        var uses = await bench.Presets.UsesAsync(CancellationToken.None);

        Assert.Equal(PresetOutcome.InUse, removed.Outcome);
        Assert.Equal("preset-in-use", removed.Code);
        Assert.Equal(1, uses[preset.Id]);
    }

    [Fact]
    public async Task AFreePresetIsRemoved()
    {
        using var bench = new Bench();
        var preset = (await bench.Presets.AddAsync(Fresh("blocked"), CancellationToken.None)).Record!;

        var removed = await bench.Presets.RemoveAsync(preset.Id, CancellationToken.None);

        Assert.True(removed.IsOk, removed.Message);
        Assert.Null(await bench.Presets.FindAsync(preset.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ATemplateKeepsItsPresetsInTheirOrder()
    {
        using var bench = new Bench();
        var first = (await bench.Presets.AddAsync(Fresh("first"), CancellationToken.None)).Record!;
        var second = (await bench.Presets.AddAsync(Fresh("second"), CancellationToken.None)).Record!;

        var added = await bench.Templates.AddAsync(
            Template("phones") with { Presets = [second.Id, first.Id, second.Id] },
            CancellationToken.None);
        var read = await bench.Templates.FindAsync(added.Record!.Id, CancellationToken.None);
        var handed = await bench.Presets.ListAsync(read!.Presets, CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        Assert.Equal([second.Id, first.Id], read.Presets);
        Assert.Equal(["second", "first"], handed.Select(preset => preset.Name));
    }

    [Fact]
    public async Task ATemplateNamingAPresetThePanelDoesNotHoldIsRefused()
    {
        using var bench = new Bench();

        var added = await bench.Templates.AddAsync(Template("phones") with { Presets = [42] }, CancellationToken.None);

        Assert.Equal(TemplateOutcome.Invalid, added.Outcome);
        Assert.Equal("unknown-preset", added.Code);
    }

    [Fact]
    public void TheRulesOfAPresetSayWhatEachEntryDoes()
    {
        var rules = PresetRules.Rules(Fresh("blocked") with { Direct = ["geoip:ru"], Block = ["domain:ads.example.com"] });

        Assert.Equal(
            ["proxy|geosite:youtube", "proxy|10.0.0.0/8", "direct|geoip:ru", "block|domain:ads.example.com"],
            rules);
    }

    [Fact]
    public async Task ACommandOnAPresetThePanelDoesNotHoldIsRefused()
    {
        using var bench = new Bench();

        var changed = await bench.Presets.ChangeAsync(9, Fresh("blocked"), CancellationToken.None);
        var removed = await bench.Presets.RemoveAsync(9, CancellationToken.None);

        Assert.Equal(PresetOutcome.Unknown, changed.Outcome);
        Assert.Equal(PresetOutcome.Unknown, removed.Outcome);
    }

    [Fact]
    public async Task EveryPresetIsGivenAnIdentifierOfItsOwn()
    {
        using var bench = new Bench();

        var first = (await bench.Presets.AddAsync(Fresh("first"), CancellationToken.None)).Record!;
        var second = (await bench.Presets.AddAsync(Fresh("second") with { Uid = first.Uid }, CancellationToken.None)).Record!;

        Assert.True(Guid.TryParseExact(first.Uid, "D", out _), first.Uid);
        Assert.True(Guid.TryParseExact(second.Uid, "D", out _), second.Uid);
        Assert.NotEqual(first.Uid, second.Uid);
    }

    [Fact]
    public async Task AChangeKeepsTheIdentifierOfAPreset()
    {
        using var bench = new Bench();
        var added = (await bench.Presets.AddAsync(Fresh("blocked"), CancellationToken.None)).Record!;

        var changed = await bench.Presets.ChangeAsync(
            added.Id,
            Fresh("renamed") with { Uid = "another", Direct = ["geoip:ru"] },
            CancellationToken.None);
        var read = await bench.Presets.FindAsync(added.Id, CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal(added.Uid, changed.Record!.Uid);
        Assert.Equal(added.Uid, read!.Uid);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("proxy")]
    [InlineData("direct")]
    [InlineData("block")]
    [InlineData("udp")]
    [InlineData("full")]
    public async Task AChangeOfTheListMovesItsTime(string what)
    {
        using var bench = new Bench();
        var added = (await bench.Presets.AddAsync(Fresh("blocked"), CancellationToken.None)).Record!;
        bench.Clock.Pass(TimeSpan.FromHours(1));

        var changed = await bench.Presets.ChangeAsync(added.Id, Changed(what), CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal(added.UpdatedUtc + TimeSpan.FromHours(1), changed.Record!.UpdatedUtc);
    }

    [Fact]
    public async Task AChangeThatLeavesTheListAsItWasKeepsItsTime()
    {
        using var bench = new Bench();
        var added = (await bench.Presets.AddAsync(Fresh("blocked"), CancellationToken.None)).Record!;
        bench.Clock.Pass(TimeSpan.FromHours(1));

        var saved = (await bench.Presets.ChangeAsync(added.Id, Fresh("blocked"), CancellationToken.None)).Record!;
        var flagged = (await bench.Presets.ChangeAsync(added.Id, Fresh("blocked") with { IsDefault = true }, CancellationToken.None)).Record!;
        var read = await bench.Presets.FindAsync(added.Id, CancellationToken.None);

        Assert.Equal(added.UpdatedUtc, saved.UpdatedUtc);
        Assert.Equal(added.UpdatedUtc, flagged.UpdatedUtc);
        Assert.False(added.IsDefault);
        Assert.True(read!.IsDefault);
    }

    [Fact]
    public void ARequestCarriesTheDefaultFlagAndTheAnswerTheIdentifier()
    {
        var draft = PresetAnswers.Draft(new PresetRequest("blocked", ["geosite:youtube"], null, null, IsDefault: true));
        var answer = PresetAnswers.Preset(draft with { Id = 7, Uid = "0f8fad5b-d9cb-469f-a165-70867728950e" }, 2);

        Assert.True(draft.IsDefault);
        Assert.False(PresetAnswers.Draft(new PresetRequest("blocked", null, null, null)).IsDefault);
        Assert.True(PresetAnswers.Draft(new PresetRequest("blocked", null, null, null), draft).IsDefault);
        Assert.False(PresetAnswers.Draft(new PresetRequest("blocked", null, null, null, IsDefault: false), draft).IsDefault);
        Assert.True(answer.IsDefault);
        Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", answer.Uid);
    }

    private static RoutingPreset Changed(string what) => what switch
    {
        "name" => Fresh("renamed"),
        "proxy" => Fresh("blocked") with { Proxy = ["geosite:youtube"] },
        "direct" => Fresh("blocked") with { Direct = ["geoip:ru"] },
        "block" => Fresh("blocked") with { Block = ["domain:ads.example.com"] },
        "udp" => Fresh("blocked") with { AllUdp = true },
        _ => Fresh("blocked") with { Full = true },
    };

    private static RoutingPreset Fresh(string name) => new()
    {
        Name = name,
        Proxy = ["geosite:youtube", "10.0.0.0/8"],
    };

    private static ClientTemplate Template(string name) => new()
    {
        Name = name,
        AllowedIps = ["0.0.0.0/0"],
    };
}
