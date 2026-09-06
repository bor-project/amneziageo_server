using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Tests;

public class RoleCatalogTests
{
    [Fact]
    public async Task TheBuiltInRoleCarriesEveryRight()
    {
        using var bench = new Bench();

        var view = await bench.Catalog.FindAsync(Roles.Admin, CancellationToken.None);

        Assert.NotNull(view);
        Assert.True(view!.Record.IsBuiltin);
        foreach (var scope in Scopes.All)
        {
            Assert.Contains(scope, view.Scopes);
        }
    }

    [Fact]
    public async Task ARoleIsAddedWithTheRightsItCarries()
    {
        using var bench = new Bench();

        var result = await bench.Catalog.AddAsync("watcher", "Watcher", [Scopes.ReadState], CancellationToken.None);

        Assert.True(result.IsOk, result.Message);

        var view = await bench.Catalog.FindAsync("watcher", CancellationToken.None);
        Assert.Equal("Watcher", view!.Record.Title);
        Assert.Equal([Scopes.ReadState], view.Scopes);
        Assert.Equal(0, view.Users);
    }

    [Fact]
    public async Task ATakenRoleNameIsRefused()
    {
        using var bench = new Bench();
        await bench.RoleAsync("watcher", Scopes.ReadState);

        var again = await bench.Catalog.AddAsync("watcher", null, [], CancellationToken.None);

        Assert.Equal(RoleOutcome.NameTaken, again.Outcome);
    }

    [Fact]
    public async Task AnUnknownRightIsRefused()
    {
        using var bench = new Bench();

        var result = await bench.Catalog.AddAsync("watcher", null, ["nothing:at:all"], CancellationToken.None);

        Assert.Equal(RoleOutcome.UnknownScope, result.Outcome);
    }

    [Fact]
    public async Task TheRightsOfARoleAreReplaced()
    {
        using var bench = new Bench();
        await bench.RoleAsync("watcher", Scopes.ReadState);

        var changed = await bench.Catalog.ChangeAsync("watcher", null, [Scopes.ManageClients], CancellationToken.None);

        Assert.True(changed.IsOk, changed.Message);

        var view = await bench.Catalog.FindAsync("watcher", CancellationToken.None);
        Assert.Equal([Scopes.ManageClients], view!.Scopes);
    }

    [Fact]
    public async Task TheBuiltInRoleKeepsItsRights()
    {
        using var bench = new Bench();

        var changed = await bench.Catalog.ChangeAsync(Roles.Admin, null, [Scopes.ReadState], CancellationToken.None);
        var removed = await bench.Catalog.RemoveAsync(Roles.Admin, CancellationToken.None);

        Assert.Equal(RoleOutcome.Builtin, changed.Outcome);
        Assert.Equal(RoleOutcome.Builtin, removed.Outcome);
    }

    [Fact]
    public async Task ARoleGivenToAnAccountIsKept()
    {
        using var bench = new Bench();
        await bench.RoleAsync("watcher", Scopes.ReadState);
        await bench.UserAsync("web", "long-enough", "watcher");

        var removed = await bench.Catalog.RemoveAsync("watcher", CancellationToken.None);

        Assert.Equal(RoleOutcome.InUse, removed.Outcome);
    }

    [Fact]
    public async Task ARoleNoAccountHoldsIsRemoved()
    {
        using var bench = new Bench();
        await bench.RoleAsync("watcher", Scopes.ReadState);

        var removed = await bench.Catalog.RemoveAsync("watcher", CancellationToken.None);

        Assert.True(removed.IsOk, removed.Message);
        Assert.Null(await bench.Catalog.FindAsync("watcher", CancellationToken.None));
    }

    [Fact]
    public async Task ARoleCountsTheAccountsItIsGivenTo()
    {
        using var bench = new Bench();
        await bench.RoleAsync("watcher", Scopes.ReadState);
        await bench.UserAsync("one", "long-enough", "watcher");
        await bench.UserAsync("two", "long-enough", "watcher");

        var view = await bench.Catalog.FindAsync("watcher", CancellationToken.None);

        Assert.Equal(2, view!.Users);
    }

    [Fact]
    public async Task AMissingRoleIsNamed()
    {
        using var bench = new Bench();

        var result = await bench.Catalog.ChangeAsync("nobody", "Nobody", null, CancellationToken.None);

        Assert.Equal(RoleOutcome.Unknown, result.Outcome);
    }
}
