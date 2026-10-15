using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Cli;
using AmneziaGeo.Server.Cli.Commands;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

/// <summary>
/// The utility gives a panel account the name of a user of the host only when told to with --host: the account then
/// stands for that user, which joins the groups of the panel the way the form of the panel has it.
/// </summary>
[Collection(PanelCommandTests.Utility)]
public sealed class UserHostTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-users-");

    public UserHostTests()
    {
        Environment.SetEnvironmentVariable(Context.KeyVariable, Path.Combine(_folder.FullName, "signing.pem"));
    }

    private string Database => Path.Combine(_folder.FullName, "server.db");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Context.HostUsersVariable, null);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public async Task ANameOfTheHost_IsRefusedWithoutHost()
    {
        Skip.IfNot(Me() is not null);
        var tools = new Tools();

        var code = await Add(tools, Me()!, "--role", "admin", "--generate", "--permanent");

        Assert.Equal(1, code);
        Assert.Empty(tools.Calls);
        Assert.Null(await Account(Me()!));
    }

    [Fact]
    public async Task ANameOfTheHostWithHost_IsAnAccountThatStandsForTheUserInTheGroupsOfThePanel()
    {
        Skip.IfNot(Me() is not null);
        var me = Me()!;
        var tools = new Tools();

        var code = await Add(tools, me, "--role", "admin", "--host", "--generate", "--permanent");
        var account = await Account(me);

        Assert.Equal(0, code);
        Assert.NotNull(account);
        Assert.Equal(PrincipalKind.Host, account.Kind);
        Assert.Equal(me, account.HostUserName);
        Assert.Contains($"{HostUserPlan.AddGroup} --force {HostUserPlan.Group}", tools.Calls);
        Assert.Contains($"{HostUserPlan.AddGroup} --force {HostUserPlan.GroupOf("admin")}", tools.Calls);
        Assert.Contains($"{HostUserPlan.ChangeUser} {string.Join(' ', HostUserPlan.Join(me, "admin"))}", tools.Calls);
    }

    [Fact]
    public async Task AHostThatRefusesTheUser_LeavesNoAccount()
    {
        Skip.IfNot(Me() is not null);
        var tools = new Tools();
        tools.Answers[$"{HostUserPlan.ChangeUser} --append"] = new CommandResult(1, string.Empty, "usermod: refused");

        var code = await Add(tools, Me()!, "--role", "admin", "--host", "--generate", "--permanent");

        Assert.Equal(1, code);
        Assert.Null(await Account(Me()!));
    }

    [Fact]
    public async Task APanelKeptFromTheUsersOfTheHost_TakesNoAccountWithHost()
    {
        Skip.IfNot(Me() is not null);
        Environment.SetEnvironmentVariable(Context.HostUsersVariable, "false");
        var tools = new Tools();

        var code = await Add(tools, Me()!, "--role", "admin", "--host", "--generate", "--permanent");

        Assert.Equal(1, code);
        Assert.Empty(tools.Calls);
        Assert.Null(await Account(Me()!));
    }

    private static string? Me() => LocalUsers.IsSupported ? LocalUsers.Current()?.Name : null;

    private async Task<int> Add(Tools tools, params string[] words)
    {
        using var context = Context.Open(Database);

        return await UserCommands.AddAsync(context, new Arguments(["user", "add", .. words]), CancellationToken.None, tools);
    }

    private async Task<AppUser?> Account(string name)
    {
        using var context = Context.Open(Database);

        return await context.Users.FindByNameAsync(name);
    }
}
