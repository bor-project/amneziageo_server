namespace AmneziaGeo.Server.Tests;

public sealed class MenuUpdatesTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("amneziageo-menu-updates-");

    public void Dispose() => _root.Delete(recursive: true);

    // The directories of the client updates server moved into the folder of the test.
    private string Layout =>
        $"updates_dir={_root.FullName}/opt; updates_data={_root.FullName}/data; updates_etc={_root.FullName}/etc; " +
        "updates_env=$updates_etc/updates.env; ";

    [Theory]
    [InlineData("\n", "panel")]
    [InlineData("1\n", "panel")]
    [InlineData("2\n", "updates")]
    [InlineData("updates\n", "updates")]
    public async Task TheInstallation_AsksWhatGoesOnTheHost(string input, string role)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, _) = await MenuQuestionsTests.RunAsync("ask_role; printf 'role %s\\n' \"${given[role]}\"", input);

        Assert.Equal(0, exit);
        Assert.Contains("  1. Panel\n  2. Client updates server\n", told, StringComparison.Ordinal);
        Assert.EndsWith($"role {role}\n", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRoleOfAFileOfAnswers_IsTakenWithoutTheChoices()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync("given[role]=updates; ask_role; printf 'role %s\\n' \"${given[role]}\"", string.Empty);

        Assert.DoesNotContain("1. Panel", told, StringComparison.Ordinal);
        Assert.Contains("What to install: updates\n", told, StringComparison.Ordinal);
        Assert.EndsWith("role updates\n", told, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("panel", "the panel is already on this host as a package: item 2 updates it")]
    [InlineData("updates", "the client updates server is already on this host as a docker: item 2 updates it")]
    public async Task AHostThatCarriesOneRole_TakesNoOther(string role, string line)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync(
            $"role() {{ printf {role}; }}; kind() {{ printf package; }}; updates_kind() {{ printf docker; }}; install_host; echo code $?",
            string.Empty);

        Assert.Contains(line + "\n", told, StringComparison.Ordinal);
        Assert.DoesNotContain("What to install", told, StringComparison.Ordinal);
        Assert.EndsWith("code 0\n", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTermsOfTheClientUpdatesServer_LeaveTheTrafficOfTheClientsOut()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, _) = await MenuQuestionsTests.RunAsync("ask_license updates", "y\n");

        Assert.Equal(0, exit);
        Assert.Contains("The releases this server hands to the apps are yours", told, StringComparison.Ordinal);
        Assert.DoesNotContain("traffic", told, StringComparison.Ordinal);
        Assert.Contains("Terms of use: https://bor-project.github.io/amneziageo_server/TERMS.html\n", told, StringComparison.Ordinal);
        Assert.Contains("Accept the terms of use? yes\n", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSettings_GiveTheServerATokenAndAPathOnce_AndNameItsPortAndCertificate()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, _) = await MenuQuestionsTests.RunAsync(
            Layout +
            "settle_updates 9445 updates.example.org; cat \"$updates_env\"; " +
            "cp \"$updates_etc/token\" \"$updates_data/token\"; cp \"$updates_etc/web-path\" \"$updates_data/web-path\"; " +
            "settle_updates 9446; " +
            "cmp -s \"$updates_etc/token\" \"$updates_data/token\" && echo same-token; " +
            "cmp -s \"$updates_etc/web-path\" \"$updates_data/web-path\" && echo same-path; " +
            "stat -c '%a %n' \"$updates_etc\" \"$updates_etc/token\" \"$updates_etc/web-path\" \"$updates_env\" | sed \"s|$updates_etc|etc|\"; " +
            "echo \"port $(updates_port) name $(updates_certificate).\"",
            string.Empty);

        Assert.Equal(0, exit);
        Assert.Contains(
            "Updates__Listen__0=*:9445\n" +
            "Updates__Certificate=/etc/letsencrypt/live/updates.example.org/fullchain.pem\n" +
            "Updates__CertificateKey=/etc/letsencrypt/live/updates.example.org/privkey.pem\n",
            told,
            StringComparison.Ordinal);
        Assert.Contains("same-token\nsame-path\n", told, StringComparison.Ordinal);
        Assert.Contains("700 etc\n600 etc/token\n600 etc/web-path\n600 etc/updates.env\n", told, StringComparison.Ordinal);
        Assert.EndsWith("port 9446 name .\n", told, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{48}$", File.ReadAllText(Path.Combine(_root.FullName, "etc", "token")).Trim());
        Assert.Matches("^[a-z0-9]{16}$", File.ReadAllText(Path.Combine(_root.FullName, "etc", "web-path")).Trim());
    }

    [Fact]
    public async Task AContainerOfItem30_HandsItsPortAndCertificateToTheSettings()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        var opt = Directory.CreateDirectory(Path.Combine(_root.FullName, "opt"));
        File.WriteAllText(Path.Combine(opt.FullName, ".env"), "AMNEZIAGEO_UPDATES_TAG=0.0.36.0\nAMNEZIAGEO_UPDATES_PORT=9447\n");
        File.WriteAllText(
            Path.Combine(opt.FullName, "compose.yaml"),
            "services:\n  updates:\n    ports:\n      - \"${AMNEZIAGEO_UPDATES_PORT:-8445}:8445\"\n    environment:\n" +
            "      - Updates__Certificate=/etc/letsencrypt/live/old.example.org/fullchain.pem\n" +
            "      - Updates__CertificateKey=/etc/letsencrypt/live/old.example.org/privkey.pem\n");

        var (exit, told, _) = await MenuQuestionsTests.RunAsync(
            Layout + "echo \"before $(updates_port) $(updates_certificate)\"; adopt_updates; cat \"$updates_env\"; write_updates_compose; " +
            "echo \"after $(updates_port) $(updates_certificate)\"; grep -c 'ports:' \"$updates_dir/compose.yaml\"",
            string.Empty);

        Assert.Equal(1, exit);
        Assert.Contains("before 9447 old.example.org\n", told, StringComparison.Ordinal);
        Assert.Contains("Updates__Listen__0=*:9447\nUpdates__Certificate=/etc/letsencrypt/live/old.example.org/fullchain.pem\n", told, StringComparison.Ordinal);
        Assert.Contains("after 9447 old.example.org\n0\n", told, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("updates.example.org", true)]
    [InlineData("", false)]
    public async Task TheComposeProject_RunsOnTheNetworkOfTheHost_AndTakesTheCertificatesOnlyWhenItAnswersUnderOne(string name, bool shared)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, _) = await MenuQuestionsTests.RunAsync(
            Layout + $"mkdir -p \"$updates_dir\"; settle_updates 8445 '{name}'; write_updates_compose; cat \"$updates_dir/compose.yaml\"",
            string.Empty);

        Assert.Equal(0, exit);
        Assert.Contains("    image: ghcr.io/bor-project/amneziageo-updates:${AMNEZIAGEO_UPDATES_TAG:-latest}\n", told, StringComparison.Ordinal);
        Assert.Contains("    network_mode: host\n", told, StringComparison.Ordinal);
        Assert.Contains($"      - path: {_root.FullName}/etc/updates.env\n        required: false\n", told, StringComparison.Ordinal);
        Assert.Contains($"      - {_root.FullName}/etc:/etc/amneziageo-updates\n", told, StringComparison.Ordinal);
        Assert.DoesNotContain("ports:", told, StringComparison.Ordinal);
        Assert.Equal(shared, told.Contains("      - /etc/letsencrypt:/etc/letsencrypt:ro\n", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("none", "8445/tcp")]
    [InlineData("keep", "8445/tcp")]
    [InlineData("new", "8445/tcp 80/tcp")]
    public async Task ThePortsOpened_AreThePortOfTheServer_AndTheOneLetsEncryptChecksANewCertificateOn(string certificate, string ports)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync($"given[port]=8445; given[certificate]={certificate}; updates_ports; echo", string.Empty);

        Assert.Equal(ports + "\n", told);
    }

    [Fact]
    public async Task TheManifest_GivesThePackageOfTheServerItsOwnDigestApartFromThePanel()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        var manifest = Path.Combine(_root.FullName, "update.json");
        var panel = new string('a', 64);
        var server = new string('b', 64);
        File.WriteAllText(
            manifest,
            $$"""
            {
              "version": "1.0.1.0",
              "packages": [
                {
                  "name": "amneziageo-server-1.0.1.0-linux-x64.tar.gz",
                  "arch": "x64",
                  "size": 1,
                  "sha256": "{{panel}}",
                  "files": {
                    "name": "amneziageo-server-1.0.1.0-linux-x64.files"
                  }
                }
              ],
              "updates": {
                "image": "ghcr.io/bor-project/amneziageo-updates@sha256:{{new string('c', 64)}}",
                "packages": [
                  {
                    "name": "amneziageo-updates-1.0.1.0-linux-x64.tar.gz",
                    "arch": "x64",
                    "size": 2,
                    "sha256": "{{server}}"
                  }
                ]
              }
            }
            """);

        var (_, told, _) = await MenuQuestionsTests.RunAsync(
            $"for file in amneziageo-updates-1.0.1.0-linux-x64.tar.gz amneziageo-server-1.0.1.0-linux-x64.tar.gz amneziageo-updates-1.0.1.0-linux-arm64.tar.gz; " +
            $"do printf '%s.\\n' \"$(manifest_sha {manifest} $file)\"; done",
            string.Empty);

        Assert.Equal($"{server}.\n{panel}.\n.\n", told);
    }

    [Theory]
    [InlineData("running", true)]
    [InlineData("stopped", false)]
    public async Task AnUpdateOfThePackage_WaitsForTheServerOnlyWhileItRuns(string state, bool waited)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, _) = await MenuQuestionsTests.RunAsync(
            $"updates_kind() {{ printf package; }}; updates_package() {{ echo \"package $1\"; }}; updates_state() {{ printf {state}; }}; " +
            "wait_updates() { echo waited; }; updates_update stable; echo code $?",
            string.Empty);

        Assert.Equal(0, exit);
        Assert.StartsWith("package stable\n", told, StringComparison.Ordinal);
        Assert.Equal(waited, told.Contains("waited\n", StringComparison.Ordinal));
        Assert.EndsWith("code 0\n", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APathOfTheWebInterface_IsSixteenLettersAndDigitsOfItsOwn()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync("for one in 1 2 3; do fresh_path; done", string.Empty);

        var paths = told.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, paths.Length);
        Assert.All(paths, one => Assert.Matches("^[a-z0-9]{16}$", one));
        Assert.Equal(3, paths.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TheAddresses_TakeTheNameOfTheCertificate_ElseTheAddressOfTheHost()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync(
            Layout + "public_address() { printf 192.0.2.10; }; settle_updates 9445; printf 'abcdefgh12345678\\n' > \"$updates_etc/web-path\"; " +
            "updates_address; echo; updates_window; echo; settle_updates 9445 updates.example.org; updates_address; echo",
            string.Empty);

        Assert.Equal("https://192.0.2.10:9445/\nhttps://192.0.2.10:9445/abcdefgh12345678/\nhttps://updates.example.org:9445/\n", told);
    }

    [Fact]
    public async Task TheMenuOfAHostOfTheServerAlone_ListsItsEighteenItems()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, _) = await MenuQuestionsTests.RunAsync("updates_status_block() { :; }; updates_main_menu", "0\n");

        Assert.Equal(0, exit);
        Assert.Contains("AmneziaGeo Client Updates Server", told, StringComparison.Ordinal);
        Assert.Contains("   7. Reset UI Path", told, StringComparison.Ordinal);
        Assert.Contains("  18. Firewall Management", told, StringComparison.Ordinal);
        Assert.DoesNotContain("19.", told, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("docker", true)]
    public async Task TheMenuOfThePanel_ShowsItem30OnlyWhileAContainerOfItStays(string kind, bool shown)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, _) = await MenuQuestionsTests.RunAsync(
            $"status_block() {{ :; }}; updates_kind() {{ printf {kind}; }}; menu",
            "0\n");

        Assert.Equal(0, exit);
        Assert.Contains("  29. DNS for Clients", told, StringComparison.Ordinal);
        Assert.Equal(shown, told.Contains("  30. Client Updates Service", StringComparison.Ordinal));
    }
}
