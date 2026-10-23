using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Tests;

public class RoleUsersTests
{
    [Fact]
    public async Task ARoleIsHandedToTheAccountsOnTheList()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        await bench.UserAsync("one", "long-enough", "viewer");
        var me = await bench.UserAsync("two", "long-enough");
        await bench.UserAsync("three", "long-enough");

        var handed = await bench.Accounts.HandRoleAsync("viewer", ["one", "three"], me.Id, CancellationToken.None);
        var view = await bench.Accounts.FindAsync("three", CancellationToken.None);

        Assert.Null(handed.Refusal);
        Assert.Equal(["three"], handed.Moved.Select(user => user.UserName));
        Assert.Equal("viewer", view!.Role);
    }

    [Fact]
    public async Task AnAccountHoldingTheRoleStaysOnTheList()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        await bench.UserAsync("one", "long-enough", "viewer");
        var me = await bench.UserAsync("two", "long-enough");
        await bench.UserAsync("three", "long-enough");

        var handed = await bench.Accounts.HandRoleAsync("viewer", ["three"], me.Id, CancellationToken.None);

        Assert.Equal(AccountOutcome.KeepsUsers, handed.Refusal!.Outcome);
        Assert.Empty(handed.Moved);
        Assert.Equal(Roles.Admin, (await bench.Accounts.FindAsync("three", CancellationToken.None))!.Role);
    }

    [Fact]
    public async Task TheCallerDoesNotTakeTheirOwnRightsAway()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        var me = await bench.UserAsync("two", "long-enough");
        await bench.UserAsync("three", "long-enough");
        await bench.UserAsync("four", "long-enough");

        var handed = await bench.Accounts.HandRoleAsync("viewer", ["three", "two"], me.Id, CancellationToken.None);

        Assert.Equal(AccountOutcome.Self, handed.Refusal!.Outcome);
        Assert.Equal(Roles.Admin, (await bench.Accounts.FindAsync("three", CancellationToken.None))!.Role);
    }

    [Fact]
    public async Task TheLastAdministratorsAreKept()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        await bench.UserAsync("two", "long-enough");
        await bench.UserAsync("three", "long-enough");

        var handed = await bench.Accounts.HandRoleAsync("viewer", ["two", "three"], actorId: 0, CancellationToken.None);

        Assert.Equal(AccountOutcome.LastAdmin, handed.Refusal!.Outcome);
        Assert.Equal(Roles.Admin, (await bench.Accounts.FindAsync("two", CancellationToken.None))!.Role);
        Assert.Equal(Roles.Admin, (await bench.Accounts.FindAsync("three", CancellationToken.None))!.Role);
    }

    [Fact]
    public async Task AnUnknownAccountIsRefusedAndNothingChanges()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        var me = await bench.UserAsync("two", "long-enough");
        await bench.UserAsync("three", "long-enough");

        var handed = await bench.Accounts.HandRoleAsync("viewer", ["three", "nobody"], me.Id, CancellationToken.None);

        Assert.Equal(AccountOutcome.Unknown, handed.Refusal!.Outcome);
        Assert.Equal(Roles.Admin, (await bench.Accounts.FindAsync("three", CancellationToken.None))!.Role);
    }

    [Fact]
    public async Task AnUnknownRoleIsRefused()
    {
        using var bench = new Bench();
        var me = await bench.UserAsync("two", "long-enough");

        var handed = await bench.Accounts.HandRoleAsync("nobody", ["two"], me.Id, CancellationToken.None);

        Assert.Equal(AccountOutcome.UnknownRole, handed.Refusal!.Outcome);
    }

    [Fact]
    public async Task TheRightsTheRoleLosesRefuseTheCallerBeforehand()
    {
        using var bench = new Bench();
        await bench.RoleAsync("ops", Scopes.ReadState, Scopes.ManageAccess);
        var me = await bench.UserAsync("two", "long-enough");
        await bench.UserAsync("three", "long-enough");

        var now = await bench.Accounts.WhyNotHandAsync("ops", ["two"], me.Id, CancellationToken.None);
        var after = await bench.Accounts.WhyNotHandAsync("ops", ["two"], [Scopes.ReadState], me.Id, CancellationToken.None);

        Assert.Null(now);
        Assert.Equal(AccountOutcome.Self, after!.Outcome);
    }

    [Fact]
    public async Task TheRightsTheRoleGainsKeepTheLastAdministrator()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        await bench.UserAsync("two", "long-enough");

        var now = await bench.Accounts.WhyNotHandAsync("viewer", ["two"], actorId: 0, CancellationToken.None);
        var after = await bench.Accounts.WhyNotHandAsync(
            "viewer", ["two"], [Scopes.ReadState, Scopes.ManageAccess], actorId: 0, CancellationToken.None);

        Assert.Equal(AccountOutcome.LastAdmin, now!.Outcome);
        Assert.Null(after);
    }

    [Fact]
    public async Task TheRightsAloneRefuseTheCallerHoldingTheRole()
    {
        using var bench = new Bench();
        await bench.RoleAsync("ops", Scopes.ReadState, Scopes.ManageAccess);
        var me = await bench.UserAsync("two", "long-enough", "ops");
        await bench.UserAsync("three", "long-enough");

        var refusal = await bench.Accounts.WhyNotRightsAsync("ops", [Scopes.ReadState], me.Id, CancellationToken.None);

        Assert.Equal(AccountOutcome.Self, refusal!.Outcome);
    }

    [Fact]
    public async Task TheRightsAloneKeepTheLastAdministrator()
    {
        using var bench = new Bench();
        await bench.RoleAsync("ops", Scopes.ReadState, Scopes.ManageAccess);
        await bench.RoleAsync("viewer", Scopes.ReadState);
        await bench.UserAsync("two", "long-enough", "ops");
        await bench.UserAsync("three", "long-enough", "viewer");

        var refusal = await bench.Accounts.WhyNotRightsAsync("ops", [Scopes.ReadState], actorId: 0, CancellationToken.None);

        Assert.Equal(AccountOutcome.LastAdmin, refusal!.Outcome);
    }

    [Fact]
    public async Task TheRightsAloneChangeWhileAnotherAdministratorStays()
    {
        using var bench = new Bench();
        await bench.RoleAsync("ops", Scopes.ReadState, Scopes.ManageAccess);
        await bench.UserAsync("one", "long-enough", "ops");
        var me = await bench.UserAsync("two", "long-enough");

        var refusal = await bench.Accounts.WhyNotRightsAsync("ops", [Scopes.ReadState], me.Id, CancellationToken.None);

        Assert.Null(refusal);
    }

    [Fact]
    public async Task TheRightsKeepingAccessPassTheCallerHoldingTheRole()
    {
        using var bench = new Bench();
        await bench.RoleAsync("ops", Scopes.ReadState, Scopes.ManageAccess);
        var me = await bench.UserAsync("two", "long-enough", "ops");

        var refusal = await bench.Accounts.WhyNotRightsAsync(
            "ops", [Scopes.ReadState, Scopes.ManageAccess, Scopes.ManageClients], me.Id, CancellationToken.None);

        Assert.Null(refusal);
    }

    [Fact]
    public async Task TheRightsOfAnUnknownRoleAreRefused()
    {
        using var bench = new Bench();
        var me = await bench.UserAsync("two", "long-enough");

        var refusal = await bench.Accounts.WhyNotRightsAsync("nobody", [Scopes.ReadState], me.Id, CancellationToken.None);

        Assert.Equal(AccountOutcome.UnknownRole, refusal!.Outcome);
    }
}
