namespace AmneziaGeo.Server.Tests;

public sealed class MenuRebootTests
{
    [Fact]
    public async Task SavedAnswersArePrivateAndAnUnsuccessfulReplacementKeepsTheOldFile()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "given[role]=panel; given[name]='phone tablet=value'; keep_answers; stat -c %a \"$answers_file\"; " +
            "given[name]=changed; mv() { return 1; }; keep_answers; echo failed:$?; " +
            "unset given; declare -A given; load_answers \"$answers_file\"; printf '%s\\n' \"${given[name]}\"; " +
            "find '" + folder.Path + "' -name 'answers.*' -print", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Equal("600\nfailed:1\nphone tablet=value\n", told);
    }

    [Fact]
    public async Task ACheckpointKeepsAnExecutableInstallerAndAPosixLoginReminder()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var source = folder.Write("original.sh", "#!/bin/bash\necho saved-installer\n");
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            $"installer_source='{source}'; given[components]='vpn proxy updates'; given[routing_defaults]=yes; keep_installer; " +
            "stat -c %a \"$answers_file\" \"$answers_file.sh\" \"$resume_notice\"; " +
            "sh -n \"$resume_notice\"; rm -- \"$installer_source\"; \"$answers_file.sh\"; cat \"$resume_notice\"", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.StartsWith("600\n700\n644\nsaved-installer\n", told);
        Assert.Contains("install\\ --resume", told, StringComparison.Ordinal);
        Assert.Contains("[ -t 1 ]", told, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    public async Task ARequiredRebootPrintsTheContinuationBeforeClosingTheSession(string answer, bool reboots)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "given[upgrade]=yes; given[role]=panel; given[components]='vpn proxy'; given[routing_defaults]=no; " +
            $"given[reboot]={answer}; apt-get() {{ :; }}; reboot_pending() {{ return 0; }}; " +
            "installation_boot() { printf boot-before; }; reboot() { echo REBOOT; }; upgrade_system; echo UNEXPECTED-CONTINUATION", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("1. Reboot this server. This SSH connection will close.", told, StringComparison.Ordinal);
        Assert.Contains("2. Installation continues automatically at boot", told, StringComparison.Ordinal);
        Assert.Contains("3. Reconnect over SSH as root: systemctl status", told, StringComparison.Ordinal);
        Assert.Contains("4. After 'done'", told, StringComparison.Ordinal);
        Assert.DoesNotContain("UNEXPECTED-CONTINUATION", told, StringComparison.Ordinal);
        Assert.Equal(reboots, told.Contains("REBOOT", StringComparison.Ordinal));
        if (reboots)
        {
            Assert.True(told.IndexOf("install --resume", StringComparison.Ordinal) < told.IndexOf("REBOOT", StringComparison.Ordinal));
        }

        var answers = File.ReadAllText(Path.Combine(folder.Path, "answers"));
        Assert.Contains("stage=upgraded\n", answers, StringComparison.Ordinal);
        Assert.Contains("reboot_boot=boot-before\n", answers, StringComparison.Ordinal);
        Assert.Contains("routing_defaults=no\n", answers, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUpgradeWithoutAPendingRebootKeepsInstalling()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "given[upgrade]=yes; apt-get() { echo APT:$*; }; reboot_pending() { return 1; }; upgrade_system; echo continued", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("continued\n", told, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder.Path, "answers")));
    }

    [Theory]
    [InlineData("before", false)]
    [InlineData("before", true)]
    [InlineData("after", true)]
    public async Task ContinuationWaitsForACompletedReboot(string boot, bool pending)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "given[stage]=upgraded; given[reboot_boot]=before; " +
            $"installation_boot() {{ printf {boot}; }}; reboot_pending() {{ return {(pending ? 0 : 1)}; }}; " +
            "apt-get() { echo UNEXPECTED-APT; }; upgrade_system", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.Contains("required reboot is not complete", error, StringComparison.Ordinal);
        Assert.DoesNotContain("UNEXPECTED-APT", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNextBootContinuesWithoutRepeatingTheSystemUpgrade()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "given[stage]=upgraded; given[reboot_boot]=before; given[channel]=test; keep_answers; " +
            "installation_boot() { printf after; }; reboot_pending() { return 1; }; apt-get() { echo UNEXPECTED-APT; }; " +
            "upgrade_system; echo continued; cat \"$answers_file\"", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("the server has rebooted; continuing installation", told, StringComparison.Ordinal);
        Assert.Contains("continued\n", told, StringComparison.Ordinal);
        Assert.Contains("channel=test\n", told, StringComparison.Ordinal);
        Assert.DoesNotContain("reboot_boot", told, StringComparison.Ordinal);
        Assert.DoesNotContain("UNEXPECTED-APT", told, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyUpgradedAnswersAreAcceptedAndStillHonorPendingReboots(bool pending)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, _) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            $"given[stage]=upgraded; installation_boot() {{ printf before; }}; reboot_pending() {{ return {(pending ? 0 : 1)}; }}; " +
            "apt-get() { echo UNEXPECTED-APT; }; upgrade_system", "").ConfigureAwait(false);

        Assert.Equal(pending ? 1 : 0, code);
        Assert.DoesNotContain("UNEXPECTED-APT", told, StringComparison.Ordinal);
        Assert.Equal(pending, File.Exists(Path.Combine(folder.Path, "answers")));
    }

    [Theory]
    [InlineData("save")]
    [InlineData("reboot")]
    public async Task FailedSavingOrRebootingDoesNotPretendToContinue(string failure)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var extra = failure == "save" ? "keep_installer() { return 1; }; reboot() { echo UNEXPECTED-REBOOT; }; "
            : "reboot() { return 17; }; ";
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "given[upgrade]=yes; given[reboot]=yes; apt-get() { :; }; reboot_pending() { return 0; }; " +
            "installation_boot() { printf before; }; " + extra + "upgrade_system", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.DoesNotContain("UNEXPECTED-REBOOT", told, StringComparison.Ordinal);
        Assert.Contains(failure == "save" ? "checkpoint could not be saved" : "reboot failed", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("panel", "package")]
    [InlineData("panel", "docker")]
    [InlineData("updates", "package")]
    [InlineData("updates", "docker")]
    public async Task ExplicitContinuationLoadsTheSavedRoleAndLayout(string role, string kind)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("answers", $"role={role}\nkind={kind}\nstage=upgraded\ncomponents=vpn proxy updates\nrouting_defaults=yes\n");
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "resume_requested=1; role() { echo already-installed; }; " +
            "install_panel() { printf 'panel:%s:%s\\n' \"${given[kind]}\" \"${given[routing_defaults]}\"; }; " +
            "install_updates() { printf 'updates:%s:%s\\n' \"${given[kind]}\" \"${given[routing_defaults]}\"; }; install_host", "n\n").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains($"{role}:{kind}:yes\n", told, StringComparison.Ordinal);
        Assert.DoesNotContain("Continue the saved installation?", told, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder.Path, "answers")));
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    [InlineData(17, true, true)]
    public async Task OnlyCompletedInstallationsRemoveTheirCheckpoint(int exit, bool complete, bool remains)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("answers", "stage=upgraded\n");
        folder.Write("answers.sh", "saved installer\n");
        folder.Write("notice.sh", "saved reminder\n");
        var (code, _, _) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            $"resume_requested=1; install_host_run() {{ installation_complete={(complete ? "yes" : "")}; return {exit}; }}; install_host", "").ConfigureAwait(false);

        Assert.Equal(exit, code);
        Assert.Equal(remains, File.Exists(Path.Combine(folder.Path, "answers")));
        Assert.Equal(remains, File.Exists(Path.Combine(folder.Path, "answers.sh")));
        Assert.Equal(remains, File.Exists(Path.Combine(folder.Path, "notice.sh")));
    }

    [Fact]
    public async Task NoninteractiveInstallDoesNotDiscardAnExistingCheckpoint()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("answers", "role=panel\nstage=upgraded\n");
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "install_host_run() { echo UNEXPECTED-INSTALL; }; install_host", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.Contains("install --resume", error, StringComparison.Ordinal);
        Assert.DoesNotContain("UNEXPECTED-INSTALL", told, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder.Path, "answers")));
    }

    [Fact]
    public async Task DecliningAContinuationKeepsTheSavedAnswers()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("answers", "role=panel\nstage=upgraded\n");
        var (code, told, _) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "on_terminal() { return 0; }; install_host_run() { echo UNEXPECTED-INSTALL; }; install_host", "n\n").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.DoesNotContain("UNEXPECTED-INSTALL", told, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder.Path, "answers")));
    }

    [Fact]
    public async Task ExplicitContinuationWithoutSavedAnswersStops()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "resume_requested=1; install_host_run() { echo UNEXPECTED-INSTALL; }; install_host", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.DoesNotContain("UNEXPECTED-INSTALL", told, StringComparison.Ordinal);
        Assert.Contains("no unfinished installation", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallResumeIsParsedEvenOnAnUpdatesHost()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "need_root() { :; }; locate_compose() { :; }; role() { printf updates; }; " +
            "install_host() { echo resumed:$resume_requested; }; main install --resume", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Equal("resumed:1\n", told);
    }

    [Fact]
    public async Task ExplicitLegacyAnswersCanContinueWithoutAnInteractivePrompt()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var answers = folder.Write("answers", "role=panel\nstage=upgraded\n");
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            $"answers_supplied='{answers}'; load_answers \"$answers_supplied\"; " +
            "install_host_run() { echo continued; }; install_host", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Equal("continued\n", told);
    }

    internal static string Layout(MenuDockerTests.Folder folder) =>
        $"answers_file='{folder.Path}/answers'; resume_notice='{folder.Path}/notice.sh'; " +
        $"resume_unit_file='{folder.Path}/resume.service'; given[generate]=yes; systemctl() {{ :; }}; ";

    [Fact]
    public async Task ACorruptCheckpointDoesNotStartAFreshInstallation()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("answers", "role=panel\n");
        var (code, told, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "resume_requested=1; install_host_run() { echo UNEXPECTED-INSTALL; }; install_host", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.DoesNotContain("UNEXPECTED-INSTALL", told, StringComparison.Ordinal);
        Assert.Contains("no completed system-upgrade stage", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStreamedLauncherIsNotSavedAsAnExecutableShellBinary()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var (code, _, error) = await MenuQuestionsTests.RunAsync(Layout(folder) +
            "installer_source=/bin/bash; keep_installer", "").ConfigureAwait(false);

        Assert.Equal(1, code);
        Assert.Contains("downloaded script file", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder.Path, "answers.sh")));
    }
}
