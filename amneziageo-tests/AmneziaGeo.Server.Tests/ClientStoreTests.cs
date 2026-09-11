using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

public class ClientStoreTests
{
    [Fact]
    public async Task AFreshInstallCarriesNoClients()
    {
        using var bench = new Bench();

        Assert.Empty(await bench.Clients.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AClientTakesThePublicKeyOfItsPrivateOne()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        var pair = Curve25519.Create();

        var added = await bench.Clients.AddAsync(
            Fresh(endpoint, "milena") with { PrivateKey = pair.PrivateKey, PublicKey = string.Empty },
            CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        Assert.Equal(pair.PublicKey, added.Record!.PublicKey);
    }

    [Fact]
    public async Task TwoClientsOfOneEndpointUnderOneNameAreRefused()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        await bench.Clients.AddAsync(Fresh(endpoint, "milena"), CancellationToken.None);

        var again = await bench.Clients.AddAsync(
            Fresh(endpoint, "milena") with { Address = ["10.8.0.9/32"] },
            CancellationToken.None);

        Assert.Equal(ClientOutcome.NameTaken, again.Outcome);
        Assert.Equal("client-name-taken", again.Code);
    }

    [Fact]
    public async Task ANameAnotherEndpointCarriesIsTakenWhateverTheCase()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        var other = await OtherAsync(bench);
        await bench.Clients.AddAsync(Fresh(endpoint, "milena"), CancellationToken.None);

        var again = await bench.Clients.AddAsync(
            Fresh(other, "Milena") with { Address = ["10.9.0.2/32"] },
            CancellationToken.None);

        Assert.Equal(ClientOutcome.NameTaken, again.Outcome);
    }

    [Fact]
    public async Task AnAddressAnotherClientCarriesIsRefused()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        await bench.Clients.AddAsync(Fresh(endpoint, "milena"), CancellationToken.None);

        var again = await bench.Clients.AddAsync(Fresh(endpoint, "bogdan"), CancellationToken.None);

        Assert.Equal(ClientOutcome.AddressTaken, again.Outcome);
    }

    [Fact]
    public async Task AnAddressOutsideTheEndpointOrReservedIsRefused()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);

        var outside = await bench.Clients.AddAsync(
            Fresh(endpoint, "milena") with { Address = ["10.9.0.2/32"] },
            CancellationToken.None);
        var own = await bench.Clients.AddAsync(
            Fresh(endpoint, "bogdan") with { Address = ["10.8.0.1/32"] },
            CancellationToken.None);

        Assert.Equal("client-address-outside", outside.Code);
        Assert.Equal("client-address-reserved", own.Code);
    }

    [Fact]
    public async Task AClientOfAnEndpointThePanelDoesNotHoldIsRefused()
    {
        using var bench = new Bench();

        var added = await bench.Clients.AddAsync(Fresh(9, "milena"), CancellationToken.None);

        Assert.Equal(ClientOutcome.UnknownConfig, added.Outcome);
    }

    [Fact]
    public async Task AClientIsTurnedOffAndBackOn()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        var added = await bench.Clients.AddAsync(Fresh(endpoint, "milena"), CancellationToken.None);

        var off = await bench.Clients.SwitchAsync(added.Record!.Id, false, CancellationToken.None);
        var on = await bench.Clients.SwitchAsync(added.Record.Id, true, CancellationToken.None);

        Assert.False(off.Record!.IsEnabled);
        Assert.True(on.Record!.IsEnabled);
    }

    [Fact]
    public async Task AClientIsReadBackAsItWasSaved()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        var added = await bench.Clients.AddAsync(
            Fresh(endpoint, "milena") with { Note = "phone", Address = ["10.8.0.2/32", "fd00::cafe:2/128"] },
            CancellationToken.None);

        var held = await bench.Clients.FindAsync(added.Record!.Id, CancellationToken.None);

        Assert.Equal("phone", held?.Note);
        Assert.Equal(["10.8.0.2/32", "fd00::cafe:2/128"], held?.Address);
    }

    [Fact]
    public async Task AClientIsRemoved()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        var added = await bench.Clients.AddAsync(Fresh(endpoint, "milena"), CancellationToken.None);

        var gone = await bench.Clients.RemoveAsync(added.Record!.Id, CancellationToken.None);

        Assert.True(gone.IsOk, gone.Message);
        Assert.Empty(await bench.Clients.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AFreeNameSkipsTheOnesThePanelCarries()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        var other = await OtherAsync(bench);
        await bench.Clients.AddAsync(Fresh(endpoint, "client"), CancellationToken.None);
        await bench.Clients.AddAsync(Fresh(other, "Client-2") with { Address = ["10.9.0.2/32"] }, CancellationToken.None);

        var free = await bench.Clients.FreeNameAsync("client", CancellationToken.None);

        Assert.Equal("client-3", free);
    }

    [Fact]
    public async Task AnImportTakesAFreeNameAndPassesAHeldKeyOver()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        await bench.Clients.AddAsync(Fresh(endpoint, "milena"), CancellationToken.None);
        var carried = Fresh(endpoint, "milena") with { Address = ["10.20.0.5/32"] };

        var taken = await bench.Clients.ImportAsync(carried, CancellationToken.None);
        var again = await bench.Clients.ImportAsync(carried, CancellationToken.None);

        Assert.True(taken.IsOk, taken.Message);
        Assert.Equal("milena-2", taken.Record!.Name);
        Assert.Equal(ClientOutcome.KeyTaken, again.Outcome);
    }

    [Fact]
    public async Task AnImportOfManyCountsWhatItTookHeldRenamedAndRefused()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        var held = Fresh(endpoint, "milena");
        await bench.Clients.AddAsync(held, CancellationToken.None);

        var report = await bench.Clients.ImportAllAsync(
            [
                held with { Name = "copy", Address = ["10.20.0.4/32"] },
                Fresh(endpoint, "milena") with { Address = ["10.20.0.5/32"] },
                Fresh(endpoint, "anna") with { Address = ["10.20.0.6/32"] },
                Fresh(endpoint, "broken") with { PrivateKey = string.Empty, PublicKey = "nope", Address = ["10.20.0.7/32"] },
            ],
            CancellationToken.None);

        Assert.Equal(2, report.Taken);
        Assert.Equal(1, report.Held);
        Assert.Equal(new[] { new ClientRename("milena", "milena-2") }, report.Renamed);
        var refused = Assert.Single(report.Refused);
        Assert.Equal("broken", refused.Name);
        Assert.Equal("bad-client-key", refused.Error);
    }

    [Fact]
    public async Task TheClientsOfAnEndpointGoWithIt()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        await bench.Clients.AddAsync(Fresh(endpoint, "milena"), CancellationToken.None);

        await bench.Configs.RemoveAsync(endpoint, CancellationToken.None);

        Assert.Empty(await bench.Clients.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheAddressesOfAnEndpointAreCountedTogether()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);
        await bench.Clients.AddAsync(
            Fresh(endpoint, "milena") with { Address = ["10.8.0.2/32", "fd00::cafe:2/128"] },
            CancellationToken.None);

        var taken = await bench.Clients.AddressesAsync(endpoint, CancellationToken.None);

        Assert.Equal(["10.8.0.2/32", "fd00::cafe:2/128"], taken);
    }

    [Fact]
    public async Task AClientWithATemplateThePanelDoesNotHoldIsRefused()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench);

        var added = await bench.Clients.AddAsync(Fresh(endpoint, "milena") with { TemplateId = 9 }, CancellationToken.None);

        Assert.Equal(ClientOutcome.UnknownTemplate, added.Outcome);
        Assert.Equal("unknown-template", added.Code);
    }

    private static async Task<long> EndpointAsync(Bench bench)
    {
        var added = await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24", "fd00::cafe:1/112"] },
            CancellationToken.None);

        return added.Record!.Id;
    }

    private static async Task<long> OtherAsync(Bench bench)
    {
        var added = await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg2") with { ListenPort = 51821, Address = ["10.9.0.1/24"] },
            CancellationToken.None);

        return added.Record!.Id;
    }

    private static TunnelClient Fresh(long endpoint, string name) =>
        ClientDefaults.Fresh(endpoint, name) with { Address = ["10.8.0.2/32"] };
}
