using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

public class TemplateStoreTests
{
    [Fact]
    public async Task ATemplateReadsBackAsItWasWritten()
    {
        using var bench = new Bench();
        var refreshed = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        var added = await bench.Templates.AddAsync(
            Fresh("blocked") with
            {
                Entries = ["10.0.0.0/8", "geoip:zz"],
                AllowedIps = ["10.0.0.0/8"],
                Missed = ["geoip:zz"],
                Mtu = 1280,
                Keepalive = 0,
                RefreshedUtc = refreshed,
            },
            CancellationToken.None);
        var read = await bench.Templates.FindAsync(added.Record!.Id, CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        Assert.Equal(["cidr:10.0.0.0/8", "geoip:zz"], read!.Entries);
        Assert.Equal(["10.0.0.0/8"], read.AllowedIps);
        Assert.Equal(["geoip:zz"], read.Missed);
        Assert.Equal(["9.9.9.9"], read.Dns);
        Assert.Equal(1280, read.Mtu);
        Assert.Equal(0, read.Keepalive);
        Assert.Equal(refreshed, read.RefreshedUtc);
    }

    [Fact]
    public async Task ATemplateWithoutValuesLeavesThemEmpty()
    {
        using var bench = new Bench();

        var added = await bench.Templates.AddAsync(new ClientTemplate { Name = "plain" }, CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        Assert.Empty(added.Record!.Entries);
        Assert.Empty(added.Record.AllowedIps);
        Assert.Empty(added.Record.Dns);
        Assert.Null(added.Record.Mtu);
        Assert.Null(added.Record.Keepalive);
        Assert.Null(added.Record.RefreshedUtc);
    }

    [Fact]
    public async Task EntriesAreKeptInOneForm()
    {
        using var bench = new Bench();

        var added = await bench.Templates.AddAsync(
            Fresh("blocked") with { Entries = ["domain:Example.com", "GEOIP:RU", "10.1.2.3/8", "example.com"] },
            CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        Assert.Equal(["domain:example.com", "geoip:ru", "cidr:10.0.0.0/8"], added.Record!.Entries);
    }

    [Fact]
    public async Task TwoTemplatesUnderOneNameAreRefused()
    {
        using var bench = new Bench();
        await bench.Templates.AddAsync(Fresh("blocked"), CancellationToken.None);

        var again = await bench.Templates.AddAsync(Fresh("blocked"), CancellationToken.None);

        Assert.Equal(TemplateOutcome.NameTaken, again.Outcome);
        Assert.Equal("name-taken", again.Code);
    }

    [Fact]
    public async Task ATakenNameIsToldBeforeAnythingIsWritten()
    {
        using var bench = new Bench();
        await bench.Templates.AddAsync(Fresh("blocked"), CancellationToken.None);

        var taken = await bench.Templates.RefuseAsync(Fresh("blocked"), 0, CancellationToken.None);
        var free = await bench.Templates.RefuseAsync(Fresh("other"), 0, CancellationToken.None);

        Assert.Equal(TemplateOutcome.NameTaken, taken!.Outcome);
        Assert.Null(free);
    }

    [Fact]
    public async Task ABadEntryIsRefusedByCode()
    {
        using var bench = new Bench();

        var added = await bench.Templates.AddAsync(
            Fresh("blocked") with { Entries = ["not a thing"] },
            CancellationToken.None);

        Assert.Equal(TemplateOutcome.Invalid, added.Outcome);
        Assert.Equal("bad-entry", added.Code);
    }

    [Fact]
    public async Task ABadRangeIsRefusedByCode()
    {
        using var bench = new Bench();

        var added = await bench.Templates.AddAsync(
            Fresh("blocked") with { AllowedIps = ["not-a-range"] },
            CancellationToken.None);

        Assert.Equal(TemplateOutcome.Invalid, added.Outcome);
        Assert.Equal("bad-allowed", added.Code);
    }

    [Fact]
    public async Task ATemplateAClientTakesIsNotRemoved()
    {
        using var bench = new Bench();
        var template = (await bench.Templates.AddAsync(Fresh("blocked"), CancellationToken.None)).Record!;
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), CancellationToken.None)).Record!;
        var client = await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "milena") with { Address = ["10.8.0.2/32"], TemplateId = template.Id },
            CancellationToken.None);

        var removed = await bench.Templates.RemoveAsync(template.Id, CancellationToken.None);
        var uses = await bench.Templates.UsesAsync(CancellationToken.None);

        Assert.True(client.IsOk, client.Message);
        Assert.Equal(template.Id, client.Record!.TemplateId);
        Assert.Equal(TemplateOutcome.InUse, removed.Outcome);
        Assert.Equal("template-in-use", removed.Code);
        Assert.Equal(1, uses[template.Id]);
    }

    [Fact]
    public async Task AFreeTemplateIsRemoved()
    {
        using var bench = new Bench();
        var template = (await bench.Templates.AddAsync(Fresh("blocked"), CancellationToken.None)).Record!;

        var removed = await bench.Templates.RemoveAsync(template.Id, CancellationToken.None);

        Assert.True(removed.IsOk, removed.Message);
        Assert.Empty(await bench.Templates.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AChangeKeepsTheNumberOfTheTemplate()
    {
        using var bench = new Bench();
        var template = (await bench.Templates.AddAsync(Fresh("blocked"), CancellationToken.None)).Record!;

        var changed = await bench.Templates.ChangeAsync(
            template.Id,
            Fresh("only-blocked") with { Dns = [] },
            CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal(template.Id, changed.Record!.Id);
        Assert.Equal("only-blocked", changed.Record.Name);
        Assert.Empty(changed.Record.Dns);
    }

    [Fact]
    public async Task ACommandOnATemplateThePanelDoesNotHoldIsRefused()
    {
        using var bench = new Bench();

        var changed = await bench.Templates.ChangeAsync(9, Fresh("blocked"), CancellationToken.None);
        var removed = await bench.Templates.RemoveAsync(9, CancellationToken.None);

        Assert.Equal(TemplateOutcome.Unknown, changed.Outcome);
        Assert.Equal(TemplateOutcome.Unknown, removed.Outcome);
    }

    private static ClientTemplate Fresh(string name) => new()
    {
        Name = name,
        Entries = ["10.0.0.0/8", "192.168.0.0/16"],
        AllowedIps = ["10.0.0.0/8", "192.168.0.0/16"],
        Dns = ["9.9.9.9"],
    };
}
