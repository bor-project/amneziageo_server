using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Tests;

public class PresetTemplatesTests
{
    [Fact]
    public async Task APresetAddedWithTemplatesComesLastInTheirPresets()
    {
        using var bench = new Bench();
        var other = await PresetAsync(bench, "other");
        var office = await TemplateAsync(bench, "office");
        var lan = await TemplateAsync(bench, "lan", other);

        var added = await bench.Presets.AddAsync(Fresh("blocked"), [office, lan], CancellationToken.None);
        var holders = await bench.Presets.HoldersAsync(CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        Assert.Equal([office, lan], holders[added.Record!.Id]);
        Assert.Equal([added.Record.Id], await PresetsOfAsync(bench, office));
        Assert.Equal([other, added.Record.Id], await PresetsOfAsync(bench, lan));
    }

    [Fact]
    public async Task AChangeHandsThePresetToTheNamedTemplatesOnly()
    {
        using var bench = new Bench();
        var blocked = await PresetAsync(bench, "blocked");
        var other = await PresetAsync(bench, "other");
        var office = await TemplateAsync(bench, "office", blocked, other);
        var lan = await TemplateAsync(bench, "lan", other);

        var changed = await bench.Presets.ChangeAsync(blocked, Fresh("blocked"), [lan], CancellationToken.None);
        var holders = await bench.Presets.HoldersAsync(CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal([lan], holders[blocked]);
        Assert.Equal([other], await PresetsOfAsync(bench, office));
        Assert.Equal([other, blocked], await PresetsOfAsync(bench, lan));
    }

    [Fact]
    public async Task ATemplateThatKeepsThePresetKeepsItsPlaceAndTime()
    {
        using var bench = new Bench();
        var blocked = await PresetAsync(bench, "blocked");
        var other = await PresetAsync(bench, "other");
        var office = await TemplateAsync(bench, "office", blocked, other);
        var held = await bench.Templates.FindAsync(office, CancellationToken.None);
        bench.Clock.Pass(TimeSpan.FromHours(1));

        var changed = await bench.Presets.ChangeAsync(blocked, Fresh("blocked"), [office], CancellationToken.None);
        var read = await bench.Templates.FindAsync(office, CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal([blocked, other], read!.Presets);
        Assert.Equal(held!.UpdatedUtc, read.UpdatedUtc);
    }

    [Fact]
    public async Task ATemplateThePresetLeavesMovesItsTime()
    {
        using var bench = new Bench();
        var blocked = await PresetAsync(bench, "blocked");
        var office = await TemplateAsync(bench, "office", blocked);
        var held = await bench.Templates.FindAsync(office, CancellationToken.None);
        bench.Clock.Pass(TimeSpan.FromHours(1));

        await bench.Presets.ChangeAsync(blocked, Fresh("blocked"), [], CancellationToken.None);
        var read = await bench.Templates.FindAsync(office, CancellationToken.None);

        Assert.Empty(read!.Presets);
        Assert.Equal(held!.UpdatedUtc + TimeSpan.FromHours(1), read.UpdatedUtc);
    }

    [Fact]
    public async Task AChangeWithoutTemplatesLeavesThemAsTheyWere()
    {
        using var bench = new Bench();
        var blocked = await PresetAsync(bench, "blocked");
        var office = await TemplateAsync(bench, "office", blocked);

        var changed = await bench.Presets.ChangeAsync(
            blocked,
            Fresh("blocked") with { Direct = ["geoip:ru"] },
            null,
            CancellationToken.None);
        var holders = await bench.Presets.HoldersAsync(CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal([office], holders[blocked]);
    }

    [Fact]
    public async Task AnUnknownTemplateIsRefusedAndNothingIsWritten()
    {
        using var bench = new Bench();
        var blocked = await PresetAsync(bench, "blocked");
        var office = await TemplateAsync(bench, "office", blocked);
        var lan = await TemplateAsync(bench, "lan");

        var changed = await bench.Presets.ChangeAsync(blocked, Fresh("renamed"), [lan, 999], CancellationToken.None);
        var holders = await bench.Presets.HoldersAsync(CancellationToken.None);

        Assert.Equal(PresetOutcome.Invalid, changed.Outcome);
        Assert.Equal("unknown-template", changed.Code);
        Assert.Equal("blocked", (await bench.Presets.FindAsync(blocked, CancellationToken.None))!.Name);
        Assert.Equal([office], holders[blocked]);
    }

    [Fact]
    public void TheAnswerNamesTheTemplatesOfThePreset()
    {
        var request = new PresetRequest("blocked", null, null, null, Templates: [4, 3, 4]);
        var answer = PresetAnswers.Preset(PresetAnswers.Draft(request) with { Id = 7 }, [3, 4]);

        Assert.Equal([4, 3], PresetAnswers.Holders(request));
        Assert.Null(PresetAnswers.Holders(new PresetRequest("blocked", null, null, null)));
        Assert.Equal(2, answer.Templates);
        Assert.Equal([3, 4], answer.TemplateIds);
    }

    [Fact]
    public async Task ATemplateKeepsItsClientsToItsPresetsOnlyWhileItNamesOne()
    {
        using var bench = new Bench();
        var blocked = await PresetAsync(bench, "blocked");

        var bare = await bench.Templates.AddAsync(
            new ClientTemplate { Name = "bare", AllowedIps = ["0.0.0.0/0"], PresetsOnly = true },
            CancellationToken.None);
        var kept = await bench.Templates.AddAsync(
            new ClientTemplate { Name = "kept", AllowedIps = ["0.0.0.0/0"], Presets = [blocked], PresetsOnly = true },
            CancellationToken.None);

        Assert.Equal(TemplateOutcome.Invalid, bare.Outcome);
        Assert.Equal("presets-only-without-presets", bare.Code);
        Assert.True(kept.IsOk, kept.Message);
        Assert.True((await bench.Templates.FindAsync(kept.Record!.Id, CancellationToken.None))!.PresetsOnly);
    }

    [Fact]
    public async Task ATemplateLeftWithoutPresetsNoLongerKeepsItsClientsToThem()
    {
        using var bench = new Bench();
        var blocked = await PresetAsync(bench, "blocked");
        var other = await PresetAsync(bench, "other");
        var office = (await bench.Templates.AddAsync(
            new ClientTemplate { Name = "office", AllowedIps = ["0.0.0.0/0"], Presets = [blocked, other], PresetsOnly = true },
            CancellationToken.None)).Record!.Id;

        await bench.Presets.ChangeAsync(blocked, Fresh("blocked"), [], CancellationToken.None);
        var one = await bench.Templates.FindAsync(office, CancellationToken.None);
        await bench.Presets.ChangeAsync(other, Fresh("other"), [], CancellationToken.None);
        var none = await bench.Templates.FindAsync(office, CancellationToken.None);

        Assert.Equal([other], one!.Presets);
        Assert.True(one.PresetsOnly);
        Assert.Empty(none!.Presets);
        Assert.False(none.PresetsOnly);
    }

    [Fact]
    public void AChangeThatLeavesTheMarkOutKeepsItWhileTheTemplateNamesAPreset()
    {
        var held = new ClientTemplate { Name = "office", Presets = [3], PresetsOnly = true, StandIns = true };

        var kept = TemplateAnswers.Change(new TemplateRequest("office", [], [], null, null, Presets: [3]), held);
        var emptied = TemplateAnswers.Change(new TemplateRequest("office", [], [], null, null, Presets: []), held);
        var asked = TemplateAnswers.Change(new TemplateRequest("office", [], [], null, null, Presets: [3], PresetsOnly: false), held);
        var answer = TemplateAnswers.Template(kept with { Id = 5 }, []);

        Assert.True(kept.PresetsOnly);
        Assert.True(kept.StandIns);
        Assert.False(emptied.PresetsOnly);
        Assert.False(asked.PresetsOnly);
        Assert.True(answer.PresetsOnly);
        Assert.True(TemplateAnswers.Draft(new TemplateRequest("office", [], [], null, null, Presets: [3], PresetsOnly: true)).PresetsOnly);
    }

    private static async Task<long> PresetAsync(Bench bench, string name) =>
        (await bench.Presets.AddAsync(Fresh(name), CancellationToken.None)).Record!.Id;

    private static async Task<long> TemplateAsync(Bench bench, string name, params long[] presets) =>
        (await bench.Templates.AddAsync(
            new ClientTemplate { Name = name, AllowedIps = ["0.0.0.0/0"], Presets = presets },
            CancellationToken.None)).Record!.Id;

    private static async Task<IReadOnlyList<long>> PresetsOfAsync(Bench bench, long template) =>
        (await bench.Templates.FindAsync(template, CancellationToken.None))!.Presets;

    private static RoutingPreset Fresh(string name) => new()
    {
        Name = name,
        Proxy = ["geosite:youtube"],
    };
}
