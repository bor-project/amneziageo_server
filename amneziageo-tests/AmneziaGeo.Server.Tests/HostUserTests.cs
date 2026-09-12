using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public class HostUserTests
{
    [Fact]
    public void TheGroupsOfAnAccountAreTheOneOfThePanelAndTheOneOfItsRole()
    {
        Assert.Equal(["amneziageo", "amneziageo-admin"], HostUserPlan.Groups("admin"));
        Assert.Equal(["amneziageo", "amneziageo-operator"], HostUserPlan.Groups("Operator"));
        Assert.Equal(["amneziageo"], HostUserPlan.Groups(null));
    }

    [Fact]
    public void AUserOfTheHostIsAddedWithAHomeAShellAndTheGroups()
    {
        Assert.Equal(
            ["--create-home", "--shell", "/bin/bash", "--groups", "amneziageo,amneziageo-admin", "milena"],
            HostUserPlan.Add("milena", "admin"));
        Assert.Equal(
            ["--append", "--groups", "amneziageo,amneziageo-admin", "milena"],
            HostUserPlan.Join("milena", "admin"));
    }

    [Fact]
    public void TheWayIntoTheHostIsClosedByALockAndAnExpiry()
    {
        Assert.Equal(["--lock", "--expiredate", "1", "milena"], HostUserPlan.Shut("milena"));
        Assert.Equal(["--unlock", "--expiredate", "", "milena"], HostUserPlan.Open("milena"));
        Assert.Equal(["--delete", "milena", "amneziageo"], HostUserPlan.Leave("milena", "amneziageo"));
    }

    [Fact]
    public void OnlyAKeyTheHostTakesIsTakenForOne()
    {
        Assert.True(HostUserPlan.IsKey("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIKm5 milena@work"));
        Assert.True(HostUserPlan.IsKey("ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABgQ=="));
        Assert.True(HostUserPlan.IsKey("ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTY="));
        Assert.False(HostUserPlan.IsKey("ssh-ed25519"));
        Assert.False(HostUserPlan.IsKey("nobody AAAAC3NzaC1lZDI1NTE5"));
        Assert.False(HostUserPlan.IsKey("-----BEGIN OPENSSH PRIVATE KEY-----"));
        Assert.False(HostUserPlan.IsKey(null));
    }

    [Fact]
    public async Task AUserTheHostDoesNotCarryIsAdded()
    {
        var tools = new Tools();
        var hosts = new HostUsers(tools, new AuthOptions(), "/tmp/amneziageo-tests/server.db");

        var sync = await hosts.CarryAsync("agtest-nobody", "admin", null, ["admin", "operator"], CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.True(tools.Called("groupadd --force amneziageo"));
        Assert.True(tools.Called("groupadd --force amneziageo-admin"));
        Assert.True(tools.Called("useradd --create-home --shell /bin/bash --groups amneziageo,amneziageo-admin agtest-nobody"));
        Assert.DoesNotContain(tools.Calls, line => line.StartsWith("usermod --append", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AUserTheHostAlreadyCarriesIsOnlyPutIntoTheGroups()
    {
        var tools = new Tools();
        var hosts = new HostUsers(tools, new AuthOptions(), "/tmp/amneziageo-tests/server.db");

        var sync = await hosts.CarryAsync("root", "admin", null, ["admin"], CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.True(tools.Called("usermod --append --groups amneziageo,amneziageo-admin root"));
        Assert.True(tools.Called("usermod --unlock --expiredate  root"));
        Assert.DoesNotContain(tools.Calls, line => line.StartsWith("useradd", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AHostThatRefusesTheUserLeavesTheReason()
    {
        var tools = new Tools();
        tools.Answers["useradd"] = new CommandResult(1, string.Empty, "useradd: name is not valid");
        var hosts = new HostUsers(tools, new AuthOptions(), "/tmp/amneziageo-tests/server.db");

        var sync = await hosts.CarryAsync("agtest-nobody", "admin", null, [], CancellationToken.None);

        Assert.False(sync.IsDone);
        Assert.Equal("useradd: name is not valid", sync.Message);
    }

    [Fact]
    public async Task AnAccountThatIsSwitchedOffCloses()
    {
        var tools = new Tools();
        var hosts = new HostUsers(tools, new AuthOptions(), "/tmp/amneziageo-tests/server.db");

        var sync = await hosts.ShutAsync("root", ["admin"], CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.True(tools.Called("usermod --lock --expiredate 1 root"));
    }

    [Fact]
    public async Task AUserTheHostDoesNotCarryIsNeitherClosedNorReleased()
    {
        var tools = new Tools();
        var hosts = new HostUsers(tools, new AuthOptions(), "/tmp/amneziageo-tests/server.db");

        Assert.True((await hosts.ShutAsync("agtest-nobody", ["admin"], CancellationToken.None)).IsDone);
        Assert.True((await hosts.ReleaseAsync("agtest-nobody", ["admin"], CancellationToken.None)).IsDone);
        Assert.Empty(tools.Calls);
    }
}
