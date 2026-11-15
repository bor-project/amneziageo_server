namespace AmneziaGeo.Server.Tests;

public sealed class MenuRoutingDefaultsTests
{
    [Theory]
    [InlineData("vpn", false)]
    [InlineData("proxy", true)]
    [InlineData("updates", false)]
    [InlineData("vpn proxy", true)]
    [InlineData("vpn updates", false)]
    [InlineData("proxy updates", true)]
    [InlineData("vpn proxy updates", true)]
    [InlineData("vpn,proxy,updates", true)]
    [InlineData("all", true)]
    public async Task OnlyInstallationsWithAProxyOfferTheDefaults(string components, bool asks)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        var (code, told, error) = await MenuQuestionsTests.RunAsync(
            $"given[components]='{components}'; ask_routing_defaults; printf 'answer=%s\\n' \"${{given[routing_defaults]:-absent}}\"", "y\n").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Equal(asks, told.Contains("Add default routing lists and presets", StringComparison.Ordinal));
        Assert.EndsWith(asks ? "answer=yes\n" : "answer=absent\n", told);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    public async Task SavedAnswersSurviveAResumeWithoutBeingAskedAgain(string answer, bool restores)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var answers = folder.Write("answers", $"components=proxy updates\nrouting_defaults={answer}\n");
        var (code, told, error) = await MenuQuestionsTests.RunAsync(
            $"answers_file='{answers}'; load_answers \"$answers_file\"; keep_answers; unset given; declare -A given; load_answers \"$answers_file\"; " +
            "cli() { echo called:$*; }; ask_routing_defaults; install_routing_defaults", "n\n").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains($"apps? {answer}\n", told, StringComparison.Ordinal);
        Assert.Equal(restores, told.Contains("called:routing defaults", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("vpn")]
    [InlineData("updates")]
    public async Task AnOldAffirmativeAnswerDoesNotSeedAComponentThatIsNowAbsent(string components)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        var (code, told, _) = await MenuQuestionsTests.RunAsync(
            $"given[components]='{components}'; given[routing_defaults]=yes; cli() {{ echo called; }}; install_routing_defaults", "").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Empty(told);
    }

    [Fact]
    public async Task AFailedRestoreStopsTheInstallationStep()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        var (code, _, _) = await MenuQuestionsTests.RunAsync(
            "given[components]=proxy; given[routing_defaults]=yes; cli() { return 17; }; install_routing_defaults", "").ConfigureAwait(false);

        Assert.Equal(1, code);
    }

    [Theory]
    [InlineData("panel", true)]
    [InlineData("updates", false)]
    public async Task LegacyInstallationRolesKeepTheirComponentMeaning(string role, bool asks)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        var (code, told, _) = await MenuQuestionsTests.RunAsync(
            $"given[role]={role}; ask_routing_defaults", "y\n").ConfigureAwait(false);

        Assert.Equal(0, code);
        Assert.Equal(asks, told.Contains("Add default routing lists", StringComparison.Ordinal));
    }
}
