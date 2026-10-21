using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

public class TemplateClientsTests
{
    [Fact]
    public async Task ATemplateAddedWithClientsIsTakenByThem()
    {
        using var bench = new Bench();
        var anna = await ClientAsync(bench, "anna", null);
        var bob = await ClientAsync(bench, "bob", null);

        var added = await bench.Templates.AddAsync(Fresh("office"), [anna, bob], CancellationToken.None);
        var members = await bench.Templates.MembersAsync(CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        Assert.Equal([anna, bob], members[added.Record!.Id]);
        Assert.Empty(await bench.Templates.PlainAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AChangeHandsTheTemplateToTheNamedClientsOnly()
    {
        using var bench = new Bench();
        var office = await TemplateAsync(bench, "office");
        var lan = await TemplateAsync(bench, "lan");
        var anna = await ClientAsync(bench, "anna", office);
        var bob = await ClientAsync(bench, "bob", office);
        var carl = await ClientAsync(bench, "carl", lan);

        var changed = await bench.Templates.ChangeAsync(office, Fresh("office"), [bob, carl], CancellationToken.None);
        var members = await bench.Templates.MembersAsync(CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal([bob, carl], members[office]);
        Assert.False(members.ContainsKey(lan));
        Assert.Equal([anna], await bench.Templates.PlainAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AChangeWithoutClientsLeavesThemAsTheyWere()
    {
        using var bench = new Bench();
        var office = await TemplateAsync(bench, "office");
        var anna = await ClientAsync(bench, "anna", office);

        var changed = await bench.Templates.ChangeAsync(
            office,
            Fresh("office") with { Dns = ["1.1.1.1"] },
            null,
            CancellationToken.None);
        var members = await bench.Templates.MembersAsync(CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal([anna], members[office]);
    }

    [Fact]
    public async Task AnUnknownClientIsRefusedAndNothingIsWritten()
    {
        using var bench = new Bench();
        var office = await TemplateAsync(bench, "office");
        var anna = await ClientAsync(bench, "anna", office);
        var bob = await ClientAsync(bench, "bob", null);

        var changed = await bench.Templates.ChangeAsync(office, Fresh("renamed"), [bob, 999], CancellationToken.None);
        var members = await bench.Templates.MembersAsync(CancellationToken.None);

        Assert.Equal(TemplateOutcome.Invalid, changed.Outcome);
        Assert.Equal("unknown-client", changed.Code);
        Assert.Equal("office", (await bench.Templates.FindAsync(office, CancellationToken.None))!.Name);
        Assert.Equal([anna], members[office]);
        Assert.Equal([bob], await bench.Templates.PlainAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ClientsLeaveTheirTemplatesForTheDefaultOne()
    {
        using var bench = new Bench();
        var office = await TemplateAsync(bench, "office");
        var anna = await ClientAsync(bench, "anna", office);
        var bob = await ClientAsync(bench, "bob", office);
        var carl = await ClientAsync(bench, "carl", null);

        var refused = await bench.Templates.FreeAsync([carl, anna], CancellationToken.None);
        var members = await bench.Templates.MembersAsync(CancellationToken.None);

        Assert.Null(refused);
        Assert.Equal([anna, carl], await bench.Templates.PlainAsync(CancellationToken.None));
        Assert.Equal([bob], members[office]);
    }

    [Fact]
    public async Task TheDefaultTemplateKeepsItsClients()
    {
        using var bench = new Bench();
        var office = await TemplateAsync(bench, "office");
        var anna = await ClientAsync(bench, "anna", office);
        var carl = await ClientAsync(bench, "carl", null);

        var refused = await bench.Templates.FreeAsync([anna], CancellationToken.None);
        var members = await bench.Templates.MembersAsync(CancellationToken.None);

        Assert.Equal("default-keeps-clients", refused!.Code);
        Assert.Equal([anna], members[office]);
        Assert.Equal([carl], await bench.Templates.PlainAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownClientIsNotFreed()
    {
        using var bench = new Bench();
        var carl = await ClientAsync(bench, "carl", null);

        var refused = await bench.Templates.FreeAsync([carl, 999], CancellationToken.None);

        Assert.Equal("unknown-client", refused!.Code);
        Assert.Equal([carl], await bench.Templates.PlainAsync(CancellationToken.None));
    }

    private static async Task<long> TemplateAsync(Bench bench, string name) =>
        (await bench.Templates.AddAsync(Fresh(name), CancellationToken.None)).Record!.Id;

    private static async Task<long> ClientAsync(Bench bench, string name, long? template)
    {
        var endpoints = await bench.Configs.ListAsync(CancellationToken.None);
        var endpoint = endpoints.Count > 0
            ? endpoints[0].Id
            : (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), CancellationToken.None)).Record!.Id;
        var held = await bench.Clients.ListAsync(CancellationToken.None);
        var added = await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint, name) with { Address = [$"10.8.0.{held.Count + 2}/32"], TemplateId = template },
            CancellationToken.None);

        Assert.True(added.IsOk, added.Message);

        return added.Record!.Id;
    }

    private static ClientTemplate Fresh(string name) => new()
    {
        Name = name,
        Entries = ["10.0.0.0/8"],
        AllowedIps = ["10.0.0.0/8"],
        Dns = ["9.9.9.9"],
    };
}
