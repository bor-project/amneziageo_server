namespace AmneziaGeo.Server.Tests;

public class MenuDockerTests
{
    [Fact]
    public async Task TheInstallation_AsksForPackageOrDockerFirst_AndStopsWithTheCommandsOfDockerWhereItLacks()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new Folder();
        var system = folder.Write("os-release", "ID=ubuntu\nVERSION_CODENAME=noble\nUBUNTU_CODENAME=noble\n");

        var (exit, told, said) = await MenuQuestionsTests.RunAsync(
            $"os_release={system}; seen[docker]=; docker_lacks() {{ printf docker; }}; ask_installation; echo went on", "y\n2\n");

        Assert.Equal(1, exit);
        Assert.Contains("Install as: 2\n", told, StringComparison.Ordinal);
        Assert.DoesNotContain("Upgrade the system", told, StringComparison.Ordinal);
        Assert.DoesNotContain("went on", told, StringComparison.Ordinal);
        Assert.Contains("Docker is not on this host", said, StringComparison.Ordinal);
        Assert.Contains("apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin\n", told, StringComparison.Ordinal);
        Assert.EndsWith("then run amneziageo-server install again\n", told, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ID=ubuntu\nVERSION_CODENAME=noble\nUBUNTU_CODENAME=noble\n", "ubuntu", "noble")]
    [InlineData("ID=debian\nVERSION_CODENAME=bookworm\n", "debian", "bookworm")]
    [InlineData("ID=linuxmint\nID_LIKE=\"ubuntu debian\"\nVERSION_CODENAME=wilma\nUBUNTU_CODENAME=noble\n", "ubuntu", "noble")]
    public async Task TheCommandsOfDocker_TakeTheRepositoryOfDockerForTheSystemOfTheHost(string release, string dist, string suite)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new Folder();
        var system = folder.Write("os-release", release);

        var (_, told, _) = await MenuQuestionsTests.RunAsync($"os_release={system}; docker_hint docker", string.Empty);

        Assert.Contains($"curl -fsSL https://download.docker.com/linux/{dist}/gpg -o /etc/apt/keyrings/docker.asc\n", told, StringComparison.Ordinal);
        Assert.Contains($"URIs: https://download.docker.com/linux/{dist}\\nSuites: {suite}\\nComponents: stable", told, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("compose", "docker-compose-plugin", "  apt-get install -y docker-compose-plugin\n")]
    [InlineData("compose", "docker-compose-v2", "  apt-get install -y docker-compose-v2\n")]
    [InlineData("daemon", "", "  systemctl enable --now docker\n")]
    public async Task AHostWithDockerInPart_IsToldTheOneCommandItLacks(string lacks, string package, string command)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new Folder();
        var system = folder.Write("os-release", "ID=debian\nVERSION_CODENAME=bookworm\n");

        var (_, told, _) = await MenuQuestionsTests.RunAsync(
            $"os_release={system}; apt_has() {{ [ \"$1\" = '{package}' ]; }}; docker_hint {lacks}", string.Empty);

        Assert.Equal(command, told);
    }

    [Theory]
    [InlineData("", "docker")]
    [InlineData("docker() { [ \"$1\" != compose ]; }; ", "compose")]
    [InlineData("docker() { [ \"$1\" != info ]; }; ", "daemon")]
    [InlineData("docker() { :; }; ", "")]
    public async Task WhatOfDockerTheHostLacks_IsNamed(string docker, string lacks)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, said) = await MenuQuestionsTests.RunAsync($"{docker}PATH=/nonexistent; printf 'lacks %s.' \"$(docker_lacks)\"", string.Empty);

        Assert.Equal($"lacks {lacks}.", told);
        Assert.Empty(said);
    }

    [Theory]
    [InlineData("srv/compose", false)]
    [InlineData("{0}/file", false)]
    [InlineData("{0}/taken", false)]
    [InlineData("{0}/other", false)]
    [InlineData("/", false)]
    [InlineData("{0}/fresh", true)]
    [InlineData("{0}/new/dir", true)]
    public async Task TheDirectoryOfCompose_IsAbsoluteAndNewOrEmpty(string path, bool fits)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new Folder();
        folder.Write("file", string.Empty);
        folder.Write("taken/compose.yaml", "name: other\n");
        folder.Write("other/notes.txt", "kept\n");
        Directory.CreateDirectory(Path.Combine(folder.Path, "fresh"));
        var named = string.Format(System.Globalization.CultureInfo.InvariantCulture, path, folder.Path);

        var (exit, _, said) = await MenuQuestionsTests.RunAsync($"compose_dir_ok '{named}'", string.Empty);

        Assert.Equal(fits ? 0 : 1, exit);
        Assert.Equal(fits, said.Length == 0);
    }

    [Theory]
    [InlineData("ra", "[ \"$1\" = up0 ]", "net/ipv6/conf/up0/accept_ra = 2\nnet.ipv4.ip_forward = 1\nnet.ipv6.conf.all.forwarding = 1\n")]
    [InlineData("ra", "return 1", "net.ipv4.ip_forward = 1\nnet.ipv6.conf.all.forwarding = 1\n")]
    [InlineData("static", ":", "net.ipv4.ip_forward = 1\nnet.ipv6.conf.all.forwarding = 1\n")]
    public async Task TheSettingsOfForwarding_KeepTheIPv6RouteTheRouterGivesBeforeForwardingTurnsOn(string origin, string takes, string lines)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync(
            $"ip() {{ printf 'default via fe80::1 dev up0 proto {origin} metric 1024 expires 1700sec hoplimit 64 pref medium\\n'; }}; " +
            $"takes_ra() {{ {takes}; }}; forwarding_lines",
            string.Empty);

        Assert.Equal(lines, told);
    }

    [Fact]
    public async Task ForwardingOnWithoutTheLinesOfItsFile_IsTurnedOnAgain()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new Folder();
        var file = folder.Write("90-amneziageo.conf", "net.ipv4.ip_forward = 1\nnet.ipv6.conf.all.forwarding = 1\n");

        var (_, told, said) = await MenuQuestionsTests.RunAsync(
            $"forwarding_conf={file}; forwarding() {{ :; }}; sysctl() {{ echo \"sysctl $*\" >&2; }}; " +
            "ip() { printf 'default via fe80::1 dev up0 proto ra\\n'; }; takes_ra() { :; }; " +
            "forwarding_kept && echo kept; forwarding_kept || forwarding_on; " +
            "forwarding_kept && echo kept now",
            string.Empty);

        Assert.DoesNotContain("kept\n", told.Replace("kept now\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal($"sysctl -p {file}\n", said);
        Assert.Contains("forwarding on, the router still gives up0 the IPv6 route\n", told, StringComparison.Ordinal);
        Assert.EndsWith("kept now\n", told, StringComparison.Ordinal);
        Assert.StartsWith("net/ipv6/conf/up0/accept_ra = 2\n", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("return 1", "iptables -P FORWARD ACCEPT\n")]
    [InlineData(":", "")]
    public async Task TheDropDockerPutsOnForwarding_IsLiftedWhereNoUfwHoldsTheChain(string ufw, string lifted)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, said) = await MenuQuestionsTests.RunAsync(
            $"docker_no_drop() {{ :; }}; ufw_active() {{ {ufw}; }}; " +
            "iptables() { if [ \"$1\" = -S ]; then echo '-P FORWARD DROP'; else echo \"iptables $*\"; fi; }; " +
            "ip6tables() { if [ \"$1\" = -S ]; then echo '-P FORWARD ACCEPT'; else echo \"ip6tables $*\"; fi; }; " +
            "docker_forward | grep -v 'forwards the packets'",
            string.Empty);

        Assert.Equal(lifted, told);
        Assert.Empty(said);
    }

    [Theory]
    [InlineData("docker", "yes", true)]
    [InlineData("docker", "no", true)]
    [InlineData("package", "yes", false)]
    public async Task UfwOfTheHost_LetsTheClientsOfTheFirstInterfaceThroughWhereTheContainerCannotGiveItRules(string kind, string resolver, bool routed)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, _, said) = await MenuQuestionsTests.RunAsync(
            "ufw() { echo \"ufw $*\" >&2; }; " +
            $"given[ufw]=yes; seen[ufw]=active; given[kind]={kind}; given[endpoint]=yes; given[endpoint_name]=awg0; " +
            $"given[endpoint_port]=51820; given[port]=8443; given[everywhere]=yes; given[resolver]={resolver}; guard_host",
            string.Empty);

        Assert.Equal(0, exit);
        Assert.Contains("ufw allow 51820/udp\n", said, StringComparison.Ordinal);
        Assert.Equal(routed, said.Contains("ufw route allow in on awg0 comment amneziageo awg0\n", StringComparison.Ordinal));
        Assert.Equal(routed, said.Contains("ufw route allow out on awg0 comment amneziageo awg0\n", StringComparison.Ordinal));
        Assert.Equal(routed && resolver == "yes", said.Contains("ufw allow in on awg0 to any port 53 proto udp comment amneziageo awg0\n", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2a01:4f8:1:2::5", "2a01:4f8:1:2::5")]
    [InlineData("fd00:1::5", "")]
    [InlineData("fe80::1", "")]
    public async Task TheIPv6OfTheHost_IsAGlobalAddressOnItsDefaultRoute(string source, string taken)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, said) = await MenuQuestionsTests.RunAsync(
            $"ip() {{ printf '2001:4860:4860::8888 from :: via fe80::1 dev up0 proto ra src {source} metric 1024 pref medium\\n'; }}; " +
            "printf 'taken %s.' \"$(public_address6)\"",
            string.Empty);

        Assert.Equal($"taken {taken}.", told);
        Assert.Empty(said);
    }

    [Fact]
    public async Task AContainer_IsAskedForTheCommandOnTheHostRightAfterItsDirectory()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new Folder();
        var directory = System.IO.Path.Combine(folder.Path, "compose");

        var (_, told, _) = await MenuQuestionsTests.RunAsync(
            $"docker_dir={directory}; seen[docker]=29; docker_lacks() {{ :; }}; " +
            "eval \"$(declare -f answer_yes | sed 1s/answer_yes/kept_answer_yes/)\"; " +
            "answer_yes() { if [ \"$1\" = upgrade ]; then echo stop; exit 0; fi; kept_answer_yes \"$@\"; }; ask_installation",
            "y\n2\n\n\n");

        Assert.Contains(
            $"Install as: 2\nDirectory for compose.yaml: {directory}\n" +
            "Put the command amneziageo-server on the host, to run the menu and the utility without entering the container? yes\nstop\n",
            told,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("yes", "", "made")]
    [InlineData("no", "#!/usr/bin/env bash\n# Manages the AmneziaGeo server on this host: the menu.\n", "gone")]
    [InlineData("no", "#!/bin/sh\necho a tool of its own\n", "kept")]
    public async Task TheMenuOfTheRelease_GoesOnTheHostOfAContainerOnlyWhenAsked(string asked, string held, string left)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new Folder();
        var self = System.IO.Path.Combine(folder.Path, "amneziageo-server");
        if (held.Length > 0)
        {
            File.WriteAllText(self, held);
        }

        var (_, told, said) = await MenuQuestionsTests.RunAsync(
            $"self={self}; given[host_menu]={asked}; kind() {{ printf docker; }}; " +
            "dcompose() { printf '#!/usr/bin/env bash\\necho the menu of the release\\n'; }; settle_self; " +
            "[ -x \"$self\" ] && echo runs",
            string.Empty);

        Assert.Empty(said);
        Assert.Equal(left, !File.Exists(self) ? "gone" : held.Length == 0 ? "made" : "kept");
        if (left == "made")
        {
            Assert.Equal("#!/usr/bin/env bash\necho the menu of the release\n", File.ReadAllText(self));
            Assert.Equal("runs\n", told);
        }
        else if (left == "kept")
        {
            Assert.Equal(held, File.ReadAllText(self));
        }
    }

    [Fact]
    public async Task AnUpdate_PutsNoMenuOnAHostThatCarriesNone()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new Folder();
        var self = System.IO.Path.Combine(folder.Path, "amneziageo-server");

        var (_, _, said) = await MenuQuestionsTests.RunAsync(
            $"self={self}; kind() {{ printf docker; }}; " +
            "dcompose() { printf '#!/usr/bin/env bash\\necho the menu of the release\\n'; }; refresh_self",
            string.Empty);

        Assert.Empty(said);
        Assert.False(File.Exists(self));
    }

    [Theory]
    [InlineData("echo \"new $AMNEZIAGEO_FOLLOWED\"", "", "new 1\n")]
    [InlineData("echo old", "", "stayed\n")]
    [InlineData("echo \"new $AMNEZIAGEO_FOLLOWED\"", "AMNEZIAGEO_FOLLOWED=1; ", "stayed\n")]
    public async Task TheMenuOnTheHost_OpensTheMenuOfTheReleaseTheContainerRunsWhereTheyDiffer(string release, string followed, string opened)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new Folder();
        var self = folder.Write("amneziageo-server", "#!/usr/bin/env bash\necho old\n");

        var (_, told, said) = await MenuQuestionsTests.RunAsync(
            $"{followed}self={self}; chmod 755 \"$self\"; BASH_ARGV0={self}; kind() {{ printf docker; }}; " +
            $"container_state() {{ printf running; }}; dcompose() {{ printf '#!/usr/bin/env bash\\n%s\\n' '{release}'; }}; " +
            "follow_release; echo stayed",
            string.Empty);

        Assert.Equal(opened, told);
        Assert.Empty(said);
    }

    [Theory]
    [InlineData("fd3a:4b5c:6d7e::1/64", "\"address\": [\"10.8.0.1/24\", \"fd3a:4b5c:6d7e::1/64\"]")]
    [InlineData("", "\"address\": [\"10.8.0.1/24\"]")]
    public async Task TheFirstInterface_TakesTheRangeOfIPv6OfItsClientsBesideItsOwn(string range, string address)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, _, said) = await MenuQuestionsTests.RunAsync(
            "api_call() { if [ \"$1\" = GET ]; then echo '{\"name\": \"awg0\", \"address\": [\"10.8.0.1/24\"], \"listenPort\": 51820}'; " +
            "else cat >&2; echo '{\"id\": 7}'; fi; }; " +
            $"given[endpoint_name]=awg0; given[endpoint_port]=51820; given[endpoint_host]=203.0.113.10; given[ipv6_range]={range}; " +
            "endpoint_calls",
            string.Empty);

        Assert.Equal(0, exit);
        Assert.Contains(address, said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRangeOfIPv6OfTheClients_IsAUniqueLocalSixtyFourOfItsOwn()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, said) = await MenuQuestionsTests.RunAsync("ula_range; echo; ula_range; echo", string.Empty);

        var ranges = told.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Empty(said);
        Assert.Equal(2, ranges.Length);
        Assert.All(ranges, range => Assert.Matches("^fd[0-9a-f]{2}:[0-9a-f]{1,4}:[0-9a-f]{1,4}::1/64$", range));
        Assert.NotEqual(ranges[0], ranges[1]);
    }

    // A directory of its own for the files of a test, taken away after it.
    internal sealed class Folder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("agmenu").FullName;

        public string Write(string name, string text)
        {
            var file = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);

            return file;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
