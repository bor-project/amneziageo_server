using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Tests;

public class AccountManagerTests
{
    [Fact]
    public async Task AnAccountIsAddedWithItsPassword()
    {
        using var bench = new Bench();
        await bench.RoleAsync("operator", Scopes.ReadState, Scopes.ManageClients);

        var result = await bench.Accounts.AddAsync("web", "Web operator", "operator", "long-enough", mustChange: true, CancellationToken.None);

        Assert.True(result.IsOk, result.Message);
        Assert.Equal("web", result.Record!.Name);

        var listed = await bench.Accounts.ListAsync(CancellationToken.None);
        Assert.Single(listed);
        Assert.True(listed[0].HasPassword);
        Assert.Equal("operator", listed[0].Role);
        Assert.Contains(Scopes.ManageClients, listed[0].Scopes);
    }

    [Fact]
    public async Task ATakenNameIsRefused()
    {
        using var bench = new Bench();
        await bench.UserAsync("web", "long-enough");

        var again = await bench.Accounts.AddAsync("web", null, Roles.Admin, "long-enough", mustChange: false, CancellationToken.None);

        Assert.Equal(AccountOutcome.NameTaken, again.Outcome);
    }

    [Fact]
    public async Task ANameOfTheWrongShapeIsRefused()
    {
        using var bench = new Bench();

        var result = await bench.Accounts.AddAsync("Web One", null, Roles.Admin, "long-enough", mustChange: false, CancellationToken.None);

        Assert.Equal(AccountOutcome.BadName, result.Outcome);
    }

    [Fact]
    public async Task AShortPasswordIsRefused()
    {
        using var bench = new Bench();

        var result = await bench.Accounts.AddAsync("web", null, Roles.Admin, "short", mustChange: false, CancellationToken.None);

        Assert.Equal(AccountOutcome.Weak, result.Outcome);
    }

    [Fact]
    public async Task AnUnknownRoleIsRefused()
    {
        using var bench = new Bench();

        var result = await bench.Accounts.AddAsync("web", null, "nobody", "long-enough", mustChange: false, CancellationToken.None);

        Assert.Equal(AccountOutcome.UnknownRole, result.Outcome);
    }

    [Fact]
    public async Task TheLastAdministratorIsKept()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        await bench.UserAsync("root-of-panel", "long-enough");

        var lowered = await bench.Accounts.SetRoleAsync("root-of-panel", "viewer", actorId: 0, CancellationToken.None);
        var switched = await bench.Accounts.SetEnabledAsync("root-of-panel", false, actorId: 0, CancellationToken.None);
        var removed = await bench.Accounts.RemoveAsync("root-of-panel", actorId: 0, CancellationToken.None);

        Assert.Equal(AccountOutcome.LastAdmin, lowered.Outcome);
        Assert.Equal(AccountOutcome.LastAdmin, switched.Outcome);
        Assert.Equal(AccountOutcome.LastAdmin, removed.Outcome);
    }

    [Fact]
    public async Task AnAdministratorIsLoweredWhenAnotherOneStays()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        await bench.UserAsync("one", "long-enough");
        await bench.UserAsync("two", "long-enough");

        var lowered = await bench.Accounts.SetRoleAsync("one", "viewer", actorId: 0, CancellationToken.None);

        Assert.True(lowered.IsOk, lowered.Message);

        var view = await bench.Accounts.FindAsync("one", CancellationToken.None);
        Assert.Equal("viewer", view!.Role);
        Assert.DoesNotContain(Scopes.ManageAccess, view.Scopes);
    }

    [Fact]
    public async Task TheCallerIsLeftAlone()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        var mine = await bench.UserAsync("one", "long-enough");
        await bench.UserAsync("two", "long-enough");
        var me = mine.Id;

        var lowered = await bench.Accounts.SetRoleAsync("one", "viewer", me, CancellationToken.None);
        var switched = await bench.Accounts.SetEnabledAsync("one", false, me, CancellationToken.None);
        var removed = await bench.Accounts.RemoveAsync("one", me, CancellationToken.None);

        Assert.Equal(AccountOutcome.Self, lowered.Outcome);
        Assert.Equal(AccountOutcome.Self, switched.Outcome);
        Assert.Equal(AccountOutcome.Self, removed.Outcome);
    }

    [Fact]
    public async Task AMissingUserIsNamed()
    {
        using var bench = new Bench();

        var result = await bench.Accounts.RemoveAsync("nobody", actorId: 0, CancellationToken.None);

        Assert.Equal(AccountOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task AnOwnPasswordIsReplacedWithTheCurrentOne()
    {
        using var bench = new Bench();
        var user = await bench.UserAsync("bor", "long enough password", Roles.Admin, mustChange: true);

        var wrong = await bench.Accounts.ChangePasswordAsync(user.Id, "not the one", "another long one", CancellationToken.None);
        var changed = await bench.Accounts.ChangePasswordAsync(user.Id, "long enough password", "another long one", CancellationToken.None);

        Assert.False(wrong.IsOk);
        Assert.True(changed.IsOk, changed.Message);
        Assert.False(changed.Record!.MustChangePassword);
    }
}
