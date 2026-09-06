using System.Security.Claims;
using AmneziaGeo.Server.Auth;
using Xunit;

namespace AmneziaGeo.Server.Tests;

public class TokenIssuerTests
{
    private static readonly Principal _principal = new(7, "bor", AuthScheme.Password, 3, new HashSet<string>(StringComparer.Ordinal) { Scopes.ReadState });

    [Fact]
    public void ATokenReadsBackAsThePrincipalItWasIssuedFor()
    {
        using var bench = new Bench();

        var token = bench.Issuer.Issue(_principal, bench.Clock.Now);
        var read = bench.Issuer.Read(token, bench.Clock.Now);

        Assert.NotNull(read);
        Assert.Equal(7, read.Id);
        Assert.Equal("bor", read.Name);
        Assert.Equal(3, read.SessionId);
        Assert.True(read.Holds(Scopes.ReadState));
    }

    [Fact]
    public void ATokenPastItsLifetimeDoesNotRead()
    {
        using var bench = new Bench();

        var token = bench.Issuer.Issue(_principal, bench.Clock.Now);

        Assert.Null(bench.Issuer.Read(token, bench.Clock.Now.Add(bench.Options.AccessLifetime).AddSeconds(1)));
    }

    [Fact]
    public void ATokenWithATouchedPayloadDoesNotRead()
    {
        using var bench = new Bench();

        var token = bench.Issuer.Issue(_principal, bench.Clock.Now);
        var parts = token.Split('.');
        var broken = string.Join('.', parts[0], parts[1][..^2] + "AA", parts[2]);

        Assert.Null(bench.Issuer.Read(broken, bench.Clock.Now));
    }

    [Fact]
    public void RubbishDoesNotRead()
    {
        using var bench = new Bench();

        Assert.Null(bench.Issuer.Read("not a token at all", bench.Clock.Now));
    }
}

public class PasswordLoginTests
{
    [Fact]
    public async Task ARightPasswordProducesBothTokens()
    {
        using var bench = new Bench();
        await bench.UserAsync("bor", "long enough password");

        var result = await bench.Login.PasswordAsync("bor", "long enough password", null, "test", CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.NotNull(result.Access);
        Assert.NotNull(result.Refresh);
        Assert.True(result.Principal!.Holds(Scopes.ManageInterfaces));
    }

    [Fact]
    public async Task AWrongPasswordProducesNothing()
    {
        using var bench = new Bench();
        await bench.UserAsync("bor", "long enough password");

        var result = await bench.Login.PasswordAsync("bor", "long enough passworD", null, "test", CancellationToken.None);

        Assert.Equal(LoginOutcome.WrongPassword, result.Outcome);
        Assert.Null(result.Access);
    }

    [Fact]
    public async Task AnUnknownUserProducesNothing()
    {
        using var bench = new Bench();

        var result = await bench.Login.PasswordAsync("nobody", "long enough password", null, "test", CancellationToken.None);

        Assert.Equal(LoginOutcome.UnknownUser, result.Outcome);
    }

    [Fact]
    public async Task ADisabledAccountDoesNotSignIn()
    {
        using var bench = new Bench();
        var record = await bench.UserAsync("bor", "long enough password");
        record.IsEnabled = false;
        await bench.Users.UpdateAsync(record);

        var result = await bench.Login.PasswordAsync("bor", "long enough password", null, "test", CancellationToken.None);

        Assert.Equal(LoginOutcome.Disabled, result.Outcome);
    }

    [Fact]
    public async Task TooManyWrongPasswordsLockTheAccount()
    {
        using var bench = new Bench();
        await bench.UserAsync("bor", "long enough password");

        for (var step = 0; step < bench.Options.FailedAttempts; step++)
        {
            await bench.Login.PasswordAsync("bor", "wrong", null, "test", CancellationToken.None);
        }

        var result = await bench.Login.PasswordAsync("bor", "long enough password", null, "test", CancellationToken.None);

        Assert.Equal(LoginOutcome.Locked, result.Outcome);
    }

    [Fact]
    public async Task ALockLiftsWhenItsTimePasses()
    {
        using var bench = new Bench();
        await bench.UserAsync("bor", "long enough password");

        for (var step = 0; step < bench.Options.FailedAttempts; step++)
        {
            await bench.Login.PasswordAsync("bor", "wrong", null, "test", CancellationToken.None);
        }

        bench.Clock.Pass(bench.Options.LockDuration + TimeSpan.FromMinutes(1));
        var result = await bench.Login.PasswordAsync("bor", "long enough password", null, "test", CancellationToken.None);

        Assert.True(result.IsOk);
    }

    [Fact]
    public async Task APasswordThatHasToChangeCarriesOnlyThatRight()
    {
        using var bench = new Bench();
        await bench.UserAsync("bor", "long enough password", Roles.Admin, mustChange: true);

        var result = await bench.Login.PasswordAsync("bor", "long enough password", null, "test", CancellationToken.None);

        Assert.True(result.MustChangePassword);
        Assert.Null(result.Refresh);
        Assert.True(result.Principal!.Holds(Scopes.ChangePassword));
        Assert.False(result.Principal.Holds(Scopes.ManageInterfaces));
    }

    [Fact]
    public async Task ARoleCarriesTheRightsItStandsFor()
    {
        using var bench = new Bench();
        await bench.RoleAsync("operator", Scopes.ReadState, Scopes.ManageClients);
        await bench.UserAsync("bor", "long enough password", "operator");

        var result = await bench.Login.PasswordAsync("bor", "long enough password", null, "test", CancellationToken.None);

        Assert.True(result.Principal!.Holds(Scopes.ManageClients));
        Assert.False(result.Principal.Holds(Scopes.ManageAccess));
    }

    [Fact]
    public async Task RightsGivenToAnAccountAreHeldOnTopOfItsRole()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        var record = await bench.UserAsync("bor", "long enough password", "viewer");
        await bench.Users.AddClaimAsync(record, new Claim(Scopes.ClaimType, Scopes.ManageClients));

        var result = await bench.Login.PasswordAsync("bor", "long enough password", null, "test", CancellationToken.None);

        Assert.True(result.Principal!.Holds(Scopes.ReadState));
        Assert.True(result.Principal.Holds(Scopes.ManageClients));
    }
}
