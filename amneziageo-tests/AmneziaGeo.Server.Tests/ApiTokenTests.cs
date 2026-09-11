using System.Net;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AmneziaGeo.Server.Tests;

public class ApiTokenTests
{
    private const string Password = "long enough password";

    [Fact]
    public async Task AMintedTokenActsByItsRole()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        var minted = await MintAsync(bench, "viewer");

        var caller = await ResolveAsync(bench, minted);

        Assert.StartsWith(ApiTokenRules.Prefix, minted.Secret, StringComparison.Ordinal);
        Assert.NotNull(caller);
        Assert.Equal("ci", caller!.Name);
        Assert.Equal("viewer", caller.Role);
        Assert.Equal(AuthScheme.ApiToken, caller.Scheme);
        Assert.Equal(0, caller.Id);
        Assert.Equal(0, caller.SessionId);
        Assert.True(caller.Holds(Scopes.ReadState));
        Assert.False(caller.Holds(Scopes.ManageClients));
        Assert.False(caller.Holds(Scopes.ChangePassword));
    }

    [Fact]
    public async Task TheDatabaseKeepsOnlyTheHashOfASecret()
    {
        using var bench = new Bench();
        var minted = await MintAsync(bench, Roles.Admin);

        var row = bench.Db.ApiTokens.Single();

        Assert.Equal(RefreshTokenStore.Fingerprint(minted.Secret), row.TokenHash);
    }

    [Fact]
    public async Task AnUnknownOrForeignTokenResolvesToNothing()
    {
        using var bench = new Bench();
        await MintAsync(bench, Roles.Admin);
        var access = bench.Issuer.Issue(new Principal(1, "bor", AuthScheme.Password, 1, new HashSet<string>()), bench.Clock.Now);

        Assert.Null(await bench.TokenStore.ResolveAsync(ApiTokenRules.Prefix + "unknown", null, CancellationToken.None));
        Assert.Null(await bench.TokenStore.ResolveAsync(access, null, CancellationToken.None));
        Assert.Null(await bench.TokenStore.ResolveAsync(string.Empty, null, CancellationToken.None));
    }

    [Fact]
    public async Task ATokenLivesItsDaysAndNoLonger()
    {
        using var bench = new Bench();
        var dated = await MintAsync(bench, Roles.Admin, "dated", days: 1);
        var endless = await MintAsync(bench, Roles.Admin, "endless");

        bench.Clock.Pass(TimeSpan.FromHours(23));
        Assert.NotNull(await ResolveAsync(bench, dated));

        bench.Clock.Pass(TimeSpan.FromHours(1));
        var listed = await bench.Tokens.ListAsync(CancellationToken.None);
        Assert.Null(await ResolveAsync(bench, dated));
        Assert.True(listed.Single(token => token.Id == dated.Token.Id).IsExpired(bench.Clock.Now));

        bench.Clock.Pass(TimeSpan.FromDays(ApiTokenRules.MaxDays));
        Assert.NotNull(await ResolveAsync(bench, endless));
    }

    [Fact]
    public async Task ARevokedTokenResolvesToNothing()
    {
        using var bench = new Bench();
        var minted = await MintAsync(bench, Roles.Admin);

        var revoked = await bench.Tokens.RevokeAsync(minted.Token.Id, actor: null, address: null, CancellationToken.None);
        var again = await bench.Tokens.RevokeAsync(minted.Token.Id, actor: null, address: null, CancellationToken.None);

        Assert.True(revoked.IsOk);
        Assert.Equal(ApiTokenOutcome.Unknown, again.Outcome);
        Assert.Null(await ResolveAsync(bench, minted));
    }

    [Fact]
    public async Task ATokenFollowsTheRightsOfItsRole()
    {
        using var bench = new Bench();
        await bench.RoleAsync("ops", Scopes.ReadState);
        var minted = await MintAsync(bench, "ops");

        await bench.Catalog.ChangeAsync("ops", null, [Scopes.ReadState, Scopes.ManageClients], CancellationToken.None);
        var caller = await ResolveAsync(bench, minted);

        Assert.True(caller!.Holds(Scopes.ManageClients));
    }

    [Fact]
    public async Task ATokenOutlivesTheAccountsOfItsRole()
    {
        using var bench = new Bench();
        await bench.RoleAsync("ops", Scopes.ReadState);
        await bench.UserAsync("keeper", Password);
        await bench.UserAsync("bot", Password, "ops");
        var minted = await MintAsync(bench, "ops");

        await bench.Accounts.RemoveAsync("bot", actorId: 0, CancellationToken.None);

        Assert.NotNull(await ResolveAsync(bench, minted));
    }

    [Fact]
    public async Task ARoleStaysWhileTokensActByIt()
    {
        using var bench = new Bench();
        await bench.RoleAsync("ops", Scopes.ReadState);
        var minted = await MintAsync(bench, "ops");

        var refused = await bench.Catalog.RemoveAsync("ops", CancellationToken.None);
        await bench.Tokens.RevokeAsync(minted.Token.Id, actor: null, address: null, CancellationToken.None);
        var removed = await bench.Catalog.RemoveAsync("ops", CancellationToken.None);

        Assert.Equal(RoleOutcome.HasTokens, refused.Outcome);
        Assert.True(removed.IsOk, removed.Message);
    }

    [Fact]
    public async Task BadNamesLifetimesRolesAndClashesAreRefused()
    {
        using var bench = new Bench();
        await MintAsync(bench, Roles.Admin);

        Assert.Equal(ApiTokenOutcome.BadName, (await TryMintAsync(bench, "  ", Roles.Admin)).Outcome);
        Assert.Equal(ApiTokenOutcome.BadName, (await TryMintAsync(bench, new string('x', ApiTokenRules.MaxNameLength + 1), Roles.Admin)).Outcome);
        Assert.Equal(ApiTokenOutcome.BadLifetime, (await TryMintAsync(bench, "zero", Roles.Admin, 0)).Outcome);
        Assert.Equal(ApiTokenOutcome.BadLifetime, (await TryMintAsync(bench, "long", Roles.Admin, ApiTokenRules.MaxDays + 1)).Outcome);
        Assert.Equal(ApiTokenOutcome.UnknownRole, (await TryMintAsync(bench, "ghost", "nobody")).Outcome);
        Assert.Equal(ApiTokenOutcome.UnknownRole, (await TryMintAsync(bench, "blank", string.Empty)).Outcome);
        Assert.Equal(ApiTokenOutcome.NameTaken, (await TryMintAsync(bench, "CI", Roles.Admin)).Outcome);
        Assert.Single(await bench.Tokens.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheLastUseIsNotedOnceAMinuteOrOnANewAddress()
    {
        using var bench = new Bench();
        var minted = await MintAsync(bench, Roles.Admin);
        var start = bench.Clock.Now;

        await bench.TokenStore.ResolveAsync(minted.Secret, "10.0.0.5", CancellationToken.None);
        bench.Clock.Pass(TimeSpan.FromSeconds(30));
        await bench.TokenStore.ResolveAsync(minted.Secret, "10.0.0.5", CancellationToken.None);
        var quiet = (await bench.Tokens.ListAsync(CancellationToken.None)).Single();

        bench.Clock.Pass(TimeSpan.FromSeconds(1));
        await bench.TokenStore.ResolveAsync(minted.Secret, "10.0.0.6", CancellationToken.None);
        var moved = (await bench.Tokens.ListAsync(CancellationToken.None)).Single();

        bench.Clock.Pass(TimeSpan.FromMinutes(1));
        await bench.TokenStore.ResolveAsync(minted.Secret, "10.0.0.6", CancellationToken.None);
        var later = (await bench.Tokens.ListAsync(CancellationToken.None)).Single();

        Assert.Equal(start, quiet.LastUsedUtc);
        Assert.Equal("10.0.0.5", quiet.LastAddress);
        Assert.Equal(start.AddSeconds(31), moved.LastUsedUtc);
        Assert.Equal("10.0.0.6", moved.LastAddress);
        Assert.Equal(start.AddSeconds(91), later.LastUsedUtc);
    }

    [Fact]
    public async Task MintAndRevokeLeaveAnAuditTrail()
    {
        using var bench = new Bench();
        var keeper = await bench.UserAsync("keeper", Password);
        var person = new Principal(keeper.Id, "keeper", AuthScheme.Password, 1, new HashSet<string>());
        var machine = new Principal(0, "deploy", AuthScheme.ApiToken, 0, new HashSet<string>(), Roles.Admin);

        var minted = (await bench.Tokens.MintAsync("ci", Roles.Admin, null, person, "10.0.0.1", CancellationToken.None)).Minted!;
        await bench.Tokens.RevokeAsync(minted.Token.Id, machine, "10.0.0.2", CancellationToken.None);

        var trail = bench.Db.AuditEntries
            .ToList()
            .Where(entry => entry.Action.StartsWith("token.", StringComparison.Ordinal))
            .OrderBy(entry => entry.Id)
            .ToList();

        Assert.Equal(new[] { "token.mint", "token.revoke" }, trail.Select(entry => entry.Action));
        Assert.Equal(keeper.Id, trail[0].UserId);
        Assert.Null(trail[1].UserId);
        Assert.Equal(AuthScheme.ApiToken, trail[1].Scheme);
        Assert.All(trail, entry => Assert.Equal("ci", entry.Target));
        Assert.DoesNotContain(trail, entry => (entry.Detail ?? string.Empty).Contains(minted.Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheBearerTakesALongLivedTokenBesideAnAccessToken()
    {
        using var bench = new Bench();
        await bench.RoleAsync("viewer", Scopes.ReadState);
        var minted = await MintAsync(bench, "viewer");
        var access = bench.Issuer.Issue(
            new Principal(1, "bor", AuthScheme.Password, 7, new HashSet<string> { Scopes.ReadState }),
            bench.Clock.Now);

        var byToken = await CallerOfAsync(bench, minted.Secret);
        var byAccess = await CallerOfAsync(bench, access);
        var byForgery = await CallerOfAsync(bench, ApiTokenRules.Prefix + "forged");

        Assert.Equal(AuthScheme.ApiToken, byToken!.Scheme);
        Assert.Equal(AuthScheme.Password, byAccess!.Scheme);
        Assert.Null(byForgery);
    }

    [Fact]
    public async Task TheBearerWritesAMappedAddressAsIPv4()
    {
        using var bench = new Bench();
        var minted = await MintAsync(bench, Roles.Admin);

        await CallerOfAsync(bench, minted.Secret, IPAddress.Parse("::ffff:10.0.0.9"));

        using var scope = bench.Scopes.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<IApiTokens>().ListAsync(CancellationToken.None);

        Assert.Equal("10.0.0.9", listed.Single().LastAddress);
    }

    private static async Task<MintedToken> MintAsync(Bench bench, string role, string name = "ci", int? days = null)
    {
        var result = await TryMintAsync(bench, name, role, days);
        Assert.True(result.IsOk, result.Message);

        return result.Minted!;
    }

    private static Task<ApiTokenResult> TryMintAsync(Bench bench, string name, string role, int? days = null) =>
        bench.Tokens.MintAsync(name, role, days, actor: null, address: null, CancellationToken.None);

    private static Task<Principal?> ResolveAsync(Bench bench, MintedToken minted) =>
        bench.TokenStore.ResolveAsync(minted.Secret, null, CancellationToken.None);

    private static async Task<Principal?> CallerOfAsync(Bench bench, string token, IPAddress? address = null)
    {
        using var scope = bench.Scopes.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Headers.Authorization = "Bearer " + token;
        http.Connection.RemoteIpAddress = address;
        var seen = default(Principal);
        var middleware = new BearerMiddleware(
            context =>
            {
                seen = context.Caller();

                return Task.CompletedTask;
            },
            bench.Issuer,
            bench.Clock);

        await middleware.InvokeAsync(http);

        return seen;
    }
}
