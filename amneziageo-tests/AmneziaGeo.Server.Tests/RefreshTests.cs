using AmneziaGeo.Server.Auth;
using Xunit;

namespace AmneziaGeo.Server.Tests;

public class RefreshTests
{
    [Fact]
    public async Task ARefreshTokenTradesForANewOne()
    {
        using var bench = new Bench();
        var first = await SignInAsync(bench);

        var rotated = await bench.Login.RefreshAsync(first.Refresh!, CancellationToken.None);

        Assert.True(rotated.IsOk);
        Assert.NotEqual(first.Refresh, rotated.Refresh);
        Assert.NotNull(rotated.Access);
    }

    [Fact]
    public async Task ASpentTokenPastTheGraceEndsTheSession()
    {
        using var bench = new Bench();
        var first = await SignInAsync(bench);
        var second = await bench.Login.RefreshAsync(first.Refresh!, CancellationToken.None);

        bench.Clock.Pass(bench.Options.RotationGrace + TimeSpan.FromSeconds(1));
        var replay = await bench.Login.RefreshAsync(first.Refresh!, CancellationToken.None);

        Assert.Equal(LoginOutcome.RefreshReplayed, replay.Outcome);

        var after = await bench.Login.RefreshAsync(second.Refresh!, CancellationToken.None);
        Assert.Equal(LoginOutcome.SessionEnded, after.Outcome);
    }

    [Fact]
    public async Task ASpentTokenInsideTheGraceStillWorks()
    {
        using var bench = new Bench();
        var first = await SignInAsync(bench);
        await bench.Login.RefreshAsync(first.Refresh!, CancellationToken.None);

        bench.Clock.Pass(TimeSpan.FromSeconds(5));
        var parallel = await bench.Login.RefreshAsync(first.Refresh!, CancellationToken.None);

        Assert.True(parallel.IsOk);
    }

    [Fact]
    public async Task AnUnknownTokenTradesForNothing()
    {
        using var bench = new Bench();
        await SignInAsync(bench);

        var result = await bench.Login.RefreshAsync("no such token", CancellationToken.None);

        Assert.Equal(LoginOutcome.RefreshUnknown, result.Outcome);
    }

    [Fact]
    public async Task ATokenPastItsLifetimeTradesForNothing()
    {
        using var bench = new Bench();
        var first = await SignInAsync(bench);

        bench.Clock.Pass(bench.Options.RefreshLifetime + TimeSpan.FromMinutes(1));
        var result = await bench.Login.RefreshAsync(first.Refresh!, CancellationToken.None);

        Assert.Equal(LoginOutcome.RefreshExpired, result.Outcome);
    }

    [Fact]
    public async Task ASessionIsNotExtendedPastItsCeiling()
    {
        var options = new AuthOptions
        {
            SessionLifetime = TimeSpan.FromHours(2),
            RefreshLifetime = TimeSpan.FromHours(1),
        };

        using var bench = new Bench(options);
        var current = await SignInAsync(bench);

        for (var step = 0; step < 2; step++)
        {
            bench.Clock.Pass(TimeSpan.FromMinutes(50));
            current = await bench.Login.RefreshAsync(current.Refresh!, CancellationToken.None);
            Assert.True(current.IsOk);
        }

        bench.Clock.Pass(TimeSpan.FromMinutes(50));
        var last = await bench.Login.RefreshAsync(current.Refresh!, CancellationToken.None);

        Assert.Equal(LoginOutcome.SessionEnded, last.Outcome);
    }

    [Fact]
    public async Task AnEndedSessionTradesForNothing()
    {
        using var bench = new Bench();
        var first = await SignInAsync(bench);

        await bench.Login.EndAsync(first.Principal!.SessionId, CancellationToken.None);
        var result = await bench.Login.RefreshAsync(first.Refresh!, CancellationToken.None);

        Assert.Equal(LoginOutcome.SessionEnded, result.Outcome);
    }

    [Fact]
    public async Task ADisabledAccountStopsRefreshing()
    {
        using var bench = new Bench();
        var first = await SignInAsync(bench);

        var record = await bench.Users.FindByNameAsync("bor");
        record!.IsEnabled = false;
        await bench.Users.UpdateAsync(record);

        var result = await bench.Login.RefreshAsync(first.Refresh!, CancellationToken.None);

        Assert.Equal(LoginOutcome.SessionEnded, result.Outcome);
    }

    [Fact]
    public async Task RefreshedRightsFollowTheAccount()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        await bench.UserAsync("bor", "long enough password", "viewer");
        await bench.UserAsync("keeper", "long enough password");
        var first = await bench.Login.PasswordAsync("bor", "long enough password", null, "test", CancellationToken.None);

        await bench.Accounts.SetRoleAsync("bor", Roles.Admin, actorId: 0, CancellationToken.None);
        var rotated = await bench.Login.RefreshAsync(first.Refresh!, CancellationToken.None);

        Assert.True(rotated.Principal!.Holds(Scopes.ManageInterfaces));
    }

    private static async Task<LoginResult> SignInAsync(Bench bench)
    {
        await bench.UserAsync("bor", "long enough password");

        return await bench.Login.PasswordAsync("bor", "long enough password", null, "test", CancellationToken.None);
    }
}

public class AccountTests
{
    [Fact]
    public async Task AnAddedAccountIsFoundByName()
    {
        using var bench = new Bench();
        await bench.RoleAsync("operator", Scopes.ReadState, Scopes.ManageClients);
        await bench.UserAsync("bor", "long enough password", "operator");

        var found = await bench.Accounts.FindAsync("bor", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("operator", found!.Role);
        Assert.Equal(PrincipalKind.Local, found.Record.Kind);
        Assert.True(found.Record.IsEnabled);
    }

    [Fact]
    public async Task AnAdministratorIsSeenAsOne()
    {
        using var bench = new Bench();

        Assert.False(await bench.Accounts.HasAdminAsync(CancellationToken.None));

        await bench.UserAsync("bor", "long enough password");

        Assert.True(await bench.Accounts.HasAdminAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ARegisteredHostUserIsFoundByItsHostName()
    {
        using var bench = new Bench();
        var registered = await bench.Accounts.RegisterHostUserAsync("bor", 1000, Roles.Admin, CancellationToken.None);

        Assert.True(registered.IsOk, registered.Message);

        var found = bench.Login.FindByHostUser("bor");

        Assert.NotNull(found);
        Assert.Equal(PrincipalKind.Host, found!.Kind);
        Assert.Equal(Roles.Admin, await bench.Access.RoleAsync(found));
    }

    [Fact]
    public async Task RemovingAnAccountTakesItsPasswordWithIt()
    {
        using var bench = new Bench();
        await bench.UserAsync("keeper", "long enough password");
        await bench.UserAsync("bor", "long enough password");

        await bench.Accounts.RemoveAsync("bor", actorId: 0, CancellationToken.None);

        Assert.Null(await bench.Users.FindByNameAsync("bor"));
    }

    [Fact]
    public async Task ANameTheHostCarriesIsRefusedShapeAside()
    {
        using var bench = new Bench();

        Assert.Null(AccountRules.CheckName("bor"));
        Assert.NotNull(AccountRules.CheckName("Bor"));
        Assert.NotNull(AccountRules.CheckName("9bor"));
        Assert.NotNull(AccountRules.CheckName(string.Empty));
        Assert.NotNull(AccountRules.CheckPassword("short"));
        Assert.Null(AccountRules.CheckPassword("long enough password"));

        await Task.CompletedTask;
    }
}
