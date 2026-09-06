using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Tests;

public class PasswordPolicyTests
{
    [Fact]
    public void APasswordShorterThanTheMinimumIsRefused()
    {
        Assert.NotNull(AccountRules.CheckPassword("bor"));
    }

    [Fact]
    public void ALoweredMinimumLetsAShortPasswordThrough()
    {
        Assert.Null(AccountRules.CheckPassword("bor", 3));
    }

    [Fact]
    public void AnEmptyPasswordIsRefusedWhateverTheMinimum()
    {
        Assert.NotNull(AccountRules.CheckPassword(string.Empty, 1));
    }

    [Fact]
    public async Task AHostAccountSignsInWithThePasswordItWasGiven()
    {
        using var bench = new Bench();
        var registered = await bench.Accounts.RegisterHostUserAsync("bor", 1000, Roles.Admin, CancellationToken.None);
        var record = registered.Record!;

        await bench.Accounts.SetPasswordAsync("bor", "correct horse battery", mustChange: false, CancellationToken.None);

        var result = await bench.Login.PasswordAsync("bor", "correct horse battery", null, null, CancellationToken.None);

        Assert.Equal(LoginOutcome.Ok, result.Outcome);
        Assert.Equal(PrincipalKind.Host, record.Kind);
        Assert.Equal(AuthScheme.Password, result.Principal!.Scheme);
        Assert.Contains(Scopes.ManageAccess, result.Principal.Scopes);
    }
}
