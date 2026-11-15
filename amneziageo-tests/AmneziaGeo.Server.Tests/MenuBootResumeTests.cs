namespace AmneziaGeo.Server.Tests;

public sealed class MenuBootResumeTests
{
    [Fact]
    public async Task TheCheckpointEnablesAOneShotBootUnitAfterTheNetwork()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var source = folder.Write("installer", "#!/bin/bash\necho saved-installer\n");
        var (code, _, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            $"installer_source='{source}'; given[role]=updates; keep_installer", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        var unit = File.ReadAllText(Path.Combine(folder.Path, "resume.service"));
        Assert.Contains("After=network-online.target", unit, StringComparison.Ordinal);
        Assert.Contains("Type=oneshot", unit, StringComparison.Ordinal);
        Assert.Contains("install --resume --boot", unit, StringComparison.Ordinal);
        Assert.Contains("WantedBy=multi-user.target", unit, StringComparison.Ordinal);
        Assert.Contains("Restart=no", unit, StringComparison.Ordinal);
        Assert.Contains("StandardOutput=null", unit, StringComparison.Ordinal);
        Assert.DoesNotContain("password", unit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureToEnableTheUnitStopsBeforeReboot()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var source = folder.Write("installer", "#!/bin/bash\ntrue\n");
        var (code, told, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            $"installer_source='{source}'; given[role]=updates; given[upgrade]=yes; given[reboot]=yes; " +
            "apt-get() { :; }; reboot_pending() { return 0; }; installation_boot() { printf before; }; " +
            "systemctl() { [ \"$1\" != enable ]; }; reboot() { echo UNEXPECTED-REBOOT; }; upgrade_system", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.Contains("checkpoint could not be saved", error, StringComparison.Ordinal);
        Assert.DoesNotContain("UNEXPECTED-REBOOT", told, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder.Path, "answers")));
    }

    [Theory]
    [InlineData(0, true, "done", false)]
    [InlineData(17, false, "failed:17", true)]
    [InlineData(0, false, "failed:1", true)]
    public async Task BootContinuationKeepsItsPrivateResultAndDoesNotLeakToTheJournal(int failure, bool complete, string state, bool checkpoint)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("answers", "stage=upgraded\nrole=updates\n");
        folder.Write("answers.sh", "saved installer\n");
        folder.Write("notice.sh", "saved notice\n");
        folder.Write("resume.service", "# written by amneziageo-server\n");
        var (code, told, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            "resume_requested=1; resume_boot=1; " +
            $"install_host_run() {{ echo fixture-secret; echo fixture-error >&2; installation_complete={(complete ? "yes" : "")}; return {failure}; }}; " +
            "install_host", "").ConfigureAwait(false);

        Assert.Equal(failure != 0 ? failure : complete ? 0 : 1, code);
        Assert.Empty(told);
        Assert.Empty(error);
        Assert.Equal(state, File.ReadAllText(Path.Combine(folder.Path, "answers.status")).Trim());
        var log = File.ReadAllText(Path.Combine(folder.Path, "answers.log"));
        Assert.Contains("fixture-secret", log, StringComparison.Ordinal);
        Assert.Contains("fixture-error", log, StringComparison.Ordinal);
        Assert.Equal(checkpoint, File.Exists(Path.Combine(folder.Path, "answers")));
        Assert.Equal(checkpoint, File.Exists(Path.Combine(folder.Path, "answers.sh")));
        Assert.Equal(checkpoint, File.Exists(Path.Combine(folder.Path, "resume.service")));
        Assert.True(File.Exists(Path.Combine(folder.Path, "notice.sh")));
        var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.Equal(privateMode, File.GetUnixFileMode(Path.Combine(folder.Path, "answers.log")));
        Assert.Equal(privateMode, File.GetUnixFileMode(Path.Combine(folder.Path, "answers.status")));
    }

    [Fact]
    public async Task AConcurrentManualOrBootAttemptDoesNotRunTheInstallation()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            "exec 9>\"$answers_file.lock\"; flock -n 9; resume_requested=1; resume_boot=1; " +
            "install_host_run() { echo UNEXPECTED-INSTALL; }; install_host", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.DoesNotContain("UNEXPECTED-INSTALL", told, StringComparison.Ordinal);
        Assert.Contains("another installation is running", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder.Path, "answers.log")));
    }

    [Fact]
    public async Task AManualRetryUpdatesThePreviousBootFailureAndKeepsTheGeneratedPassword()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("answers", "stage=upgraded\nrole=panel\nlogin=fixture-owner\n");
        folder.Write("answers.log", "old boot failure\n");
        folder.Write("answers.status", "failed:17\n");
        var (code, _, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            "resume_requested=1; install_host_run() { made_password=fixture-generated; installation_complete=1; }; install_host", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Equal("done", File.ReadAllText(Path.Combine(folder.Path, "answers.status")).Trim());
        var log = File.ReadAllText(Path.Combine(folder.Path, "answers.log"));
        Assert.Contains("Manual continuation completed", log, StringComparison.Ordinal);
        Assert.Contains("fixture-generated", log, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder.Path, "answers")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(folder.Path, "answers.log")));
    }

    [Fact]
    public async Task FailureToSaveTheFinalStatusRetainsTheCheckpoint()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("answers", "stage=upgraded\nrole=updates\n");
        folder.Write("answers.sh", "saved installer\n");
        var (code, _, _) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            "resume_requested=1; resume_boot=1; installation_status() { [ \"$1\" != done ]; }; " +
            "install_host_run() { installation_complete=1; }; install_host", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.True(File.Exists(Path.Combine(folder.Path, "answers")));
        Assert.True(File.Exists(Path.Combine(folder.Path, "answers.sh")));
    }

    [Fact]
    public async Task TheChosenPasswordIsCollectedBeforeRebootAndPassedOnlyOnStandardInput()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, _) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            "given[role]=panel; given[generate]=no; given[login]=owner; given[stage]=upgraded; installation_resumed=1; " +
            "on_terminal() { return 0; }; installation_password; keep_answers; " +
            "cli_in() { read -r a; read -r b; [ \"$a\" = fixture-password ] && [ \"$a\" = \"$b\" ]; }; " +
            "first_admin; echo code:$?; cat \"$answers_file\"", "fixture-password\nfixture-password\n").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Contains("code:0", told, StringComparison.Ordinal);
        Assert.Contains("admin_created=yes", told, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-password", told, StringComparison.Ordinal);
        Assert.DoesNotContain("first_password", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChosenPasswordWithoutATerminalCannotRebootIntoAnUnanswerablePrompt()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, _, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            "given[role]=panel; given[generate]=no; installation_password", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.Contains("chosen password before reboot", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder.Path, "answers")));
    }

    [Fact]
    public async Task MissingPasswordRetainsTheAnswersAndInstallerWithoutEnablingBoot()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var source = folder.Write("installer", "#!/bin/bash\ntrue\n");
        var (code, _, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            $"installer_source='{source}'; given[role]=panel; given[generate]=no; given[stage]=upgraded; keep_installer", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.Contains("continue as root", error, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder.Path, "answers")));
        Assert.True(File.Exists(Path.Combine(folder.Path, "answers.sh")));
        Assert.False(File.Exists(Path.Combine(folder.Path, "resume.service")));
    }

    [Fact]
    public async Task AResumedAdministratorKeepsTheSameGeneratedPasswordAndIsNotCreatedTwice()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            "given[login]=owner; given[stage]=upgraded; installation_resumed=1; keep_answers; " +
            "cli() { echo call >>\"$answers_file.calls\"; echo 'password: fixture-generated-password'; }; " +
            "first_admin; made_password=; unset given; declare -A given; load_answers \"$answers_file\"; first_admin; " +
            "wc -l <\"$answers_file.calls\"; [ \"$made_password\" = fixture-generated-password ]", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.EndsWith("1\n", told);
        Assert.Contains("admin_created=yes", File.ReadAllText(Path.Combine(folder.Path, "answers")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInternalBootFlagRequiresExplicitResume()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, _, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) + "resume_boot=1; install_host", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.Contains("requires --resume alone", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MainParsesBothBootContinuationFlags()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            "need_root() { :; }; locate_compose() { :; }; install_host() { echo flags:$resume_requested:$resume_boot; }; main install --resume --boot", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Equal("flags:1:1\n", told);
    }

    [Theory]
    [InlineData("package")]
    [InlineData("docker")]
    public async Task AResumedPanelUsesItsSavedQuestionsAndComposeDirectory(string kind)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            $"installation_resumed=1; given[questions_saved]=yes; given[kind]={kind}; given[restore]=; given[channel]=stable; given[docker_dir]='{folder.Path}/compose'; " +
            "assess_host() { :; }; ask_installation() { echo UNEXPECTED-QUESTIONS; return 1; }; plan() { :; }; answer_yes() { :; }; " +
            "upgrade_system() { :; }; prepare_host() { :; }; install_package() { :; }; install_container() { :; }; wait_panel() { :; }; " +
            "settle_self() { :; }; start_panel() { :; }; put_shortcut() { :; }; first_admin() { :; }; place_panel() { :; }; " +
            "install_routing_defaults() { :; }; first_endpoint() { :; }; guard_host() { :; }; install_doors() { :; }; final_check() { :; }; " +
            "install_panel; echo complete:$installation_complete; echo directory:$docker_dir", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.DoesNotContain("UNEXPECTED-QUESTIONS", told, StringComparison.Ordinal);
        Assert.Contains("complete:1", told, StringComparison.Ordinal);
        if (kind == "docker")
        {
            Assert.Contains($"directory:{folder.Path}/compose", told, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("package")]
    [InlineData("docker")]
    public async Task AResumedUpdatesServerUsesItsSavedQuestions(string kind)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("token", "fixture-token");
        var (code, told, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            $"installation_resumed=1; given[questions_saved]=yes; given[kind]={kind}; given[channel]=stable; given[certificate]=none; given[port]=8445; updates_etc='{folder.Path}'; " +
            "assess_updates_host() { :; }; ask_updates() { echo UNEXPECTED-QUESTIONS; return 1; }; plan_updates() { :; }; " +
            "answer_yes() { :; }; upgrade_system() { :; }; apt-get() { :; }; settle_updates() { :; }; " +
            "updates_package() { :; }; updates_container() { :; }; wait_updates() { :; }; put_shortcut() { :; }; " +
            "guard_updates() { :; }; updates_address() { printf fixture; }; updates_window() { printf fixture; }; " +
            "install_updates; echo complete:$installation_complete", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.DoesNotContain("UNEXPECTED-QUESTIONS", told, StringComparison.Ordinal);
        Assert.Contains("complete:1", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCompletedFirstEndpointDoesNotCreateASecondClient()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            "given[endpoint]=yes; given[endpoint_created]=yes; given[first_client_file]=fixture.conf; given[first_client_link]=fixture-link; " +
            "cli() { echo UNEXPECTED-API; return 1; }; first_endpoint; echo file:$made_client; echo link:$made_link", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.DoesNotContain("UNEXPECTED-API", told, StringComparison.Ordinal);
        Assert.Contains("file:fixture.conf", told, StringComparison.Ordinal);
        Assert.Contains("link:fixture-link", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BootContinuationRestoresCustomLayoutWithoutPublishingItsEnvironmentInTheUnit()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var source = folder.Write("installer", "#!/bin/bash\ntrue\n");
        var (code, told, error) = await MenuQuestionsTests.RunAsync(MenuRebootTests.Layout(folder) +
            $"installer_source='{source}'; given[role]=updates; given[stage]=upgraded; " +
            "AMNEZIAGEO_DATA='/fixture/data path'; AMNEZIAGEO_CLI='fixture-cli --value=one=two'; AMNEZIAGEO_UNIT=fixture-server; " +
            "AMNEZIAGEO_DOCKER_DIR=/fixture/compose; AMNEZIAGEO_UPDATES_DIR=/fixture/updates; keep_installer; " +
            "unset AMNEZIAGEO_DATA AMNEZIAGEO_CLI AMNEZIAGEO_UNIT AMNEZIAGEO_DOCKER_DIR AMNEZIAGEO_UPDATES_DIR; " +
            "unset given; declare -A given; resume_requested=1; resume_answers; " +
            "[ \"$data\" = '/fixture/data path' ] && [ \"$unit\" = fixture-server ] && [ \"$docker_dir\" = /fixture/compose ] " +
            "&& [ \"$updates_dir\" = /fixture/updates ] && [ \"$AMNEZIAGEO_CLI\" = 'fixture-cli --value=one=two' ] && echo restored", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("restored", told, StringComparison.Ordinal);
        var unit = File.ReadAllText(Path.Combine(folder.Path, "resume.service"));
        Assert.DoesNotContain("fixture-cli", unit, StringComparison.Ordinal);
        Assert.DoesNotContain("/fixture/data path", unit, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-cli", File.ReadAllText(Path.Combine(folder.Path, "answers")), StringComparison.Ordinal);
    }
}
