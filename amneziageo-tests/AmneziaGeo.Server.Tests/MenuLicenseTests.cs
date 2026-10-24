namespace AmneziaGeo.Server.Tests;

public class MenuLicenseTests
{
    [Fact]
    public async Task TheInstallation_AsksForTheTermsFirst_AndStopsWithoutTheConsent()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, said) = await MenuQuestionsTests.RunAsync("ask_installation || exit 1; echo went on", "n\n");

        Assert.Equal(1, exit);
        Assert.Contains("provided as is, without any warranty", told, StringComparison.Ordinal);
        Assert.Contains("Terms of use: https://bor-project.github.io/amneziageo_server/TERMS.html\n", told, StringComparison.Ordinal);
        Assert.Contains("Accept the terms of use? no\n", told, StringComparison.Ordinal);
        Assert.DoesNotContain("Install as", told, StringComparison.Ordinal);
        Assert.DoesNotContain("went on", told, StringComparison.Ordinal);
        Assert.Contains("the terms of use are not accepted, nothing is installed", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutATerminalAndWithoutAnAnswer_TheInstallationStops()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, said) = await MenuQuestionsTests.RunAsync("ask_license || exit 1; echo went on", string.Empty);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("went on", told, StringComparison.Ordinal);
        Assert.Contains("license=yes in a file of answers accepts them", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheConsentOfAFileOfAnswers_GoesOnWithoutTheText()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, _) = await MenuQuestionsTests.RunAsync("given[license]=yes; ask_license; echo went on", string.Empty);

        Assert.Equal(0, exit);
        Assert.Contains("Accept the terms of use? yes\n", told, StringComparison.Ordinal);
        Assert.DoesNotContain("without any warranty", told, StringComparison.Ordinal);
        Assert.EndsWith("went on\n", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheConsentOnATerminal_GoesOnToTheKindOfTheInstallation()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync(
            "eval \"$(declare -f answer | sed 1s/answer/kept_answer/)\"; " +
            "answer() { if [ \"$1\" = kind ]; then printf 'license=%s\\n' \"${given[license]}\"; exit 0; fi; kept_answer \"$@\"; }; ask_installation",
            "y\n");

        Assert.Contains("Accept the terms of use? yes\n", told, StringComparison.Ordinal);
        Assert.EndsWith("license=yes\n", told, StringComparison.Ordinal);
    }
}
