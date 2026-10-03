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
